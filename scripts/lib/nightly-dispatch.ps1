#requires -Version 5.1
# Diagnostic scheduling only. Canonical chunks remain the planner's own objects.
function Resolve-NightlyPreviousRunOrder {
    param([object[]]$Entries, $PreviousRun, [string]$CandidateSha, [datetime]$NowUtc)
    # This is a preference for a NEW complete attempt, never execution resume.
    # Missing rows are unfinished. Rotate each lane after its last observed pass,
    # wrapping earlier holes back into the unfinished partition.
    $reason='previous-run-missing'
    $lanes=[Collections.Generic.List[object]]::new()
    try {
        if ($null -eq $PreviousRun) { throw $reason }
        if ($PreviousRun.PSObject.Properties.Name -ccontains 'liveness' -and $PreviousRun.liveness -cne 'closed') { throw 'previous-run-liveness-unknown' }
        $reason='previous-run-schema'
        if ($PreviousRun.suites -isnot [array] -or [string]$PreviousRun.sha -cnotmatch '^[a-f0-9]{40}$') { throw $reason }
        if ($PreviousRun.sha -cne $CandidateSha) { throw 'previous-run-candidate-mismatch' }
        $completed=[datetimeoffset]::MinValue
        if($PreviousRun.completedAt -is [datetime]){$completed=[datetimeoffset]$PreviousRun.completedAt}
        elseif (-not [datetimeoffset]::TryParse([string]$PreviousRun.completedAt,[ref]$completed)) { throw $reason }
        if ($completed.UtcDateTime -gt $NowUtc.ToUniversalTime().AddMinutes(5) -or $completed.UtcDateTime -lt $NowUtc.ToUniversalTime().AddDays(-7)) { throw 'previous-run-stale' }
        $known=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach($entry in $Entries){if(-not $known.Add($entry.suite+[char]0+$entry.chunk)){throw 'canonical-chunk-duplicate'}}
        $states=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
        $lastPassed=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
        foreach($row in $PreviousRun.suites){
            $key=[string]$row.id+[char]0+[string]$row.chunk
            if(-not $known.Contains($key)){throw 'previous-run-roster-mismatch'}
            if($states.ContainsKey($key)){throw 'previous-run-duplicate'}
            $state='unfinished'
            if($row.timedOut -eq $true -or $row.result -cin @('FAIL','Failed','TIMEOUT','Error') -or ($null -ne $row.exitCode -and [int]$row.exitCode -ne 0)){$state='failed'}
            elseif($row.result -cin @('pass','Passed','PASS') -and $null -ne $row.exitCode -and [int]$row.exitCode -eq 0 -and $row.skipped -ne $true -and $row.coverageComplete -ne $false){$state='passed';$lastPassed[[string]$row.id]=[string]$row.chunk}
            elseif($row.result -cnotin @('unstarted','incomplete','cancelled','Skipped','skip','pass','Passed','PASS')){throw 'previous-run-state'}
            $states.Add($key,$state)
        }
        $failed=[Collections.Generic.List[object]]::new();$unfinished=[Collections.Generic.List[object]]::new();$passed=[Collections.Generic.List[object]]::new()
        foreach($entry in $Entries){$key=$entry.suite+[char]0+$entry.chunk;if($entry.priorFailure -ne $true -and $states.ContainsKey($key) -and $states[$key] -ceq 'passed'){$passed.Add($entry)}}
        foreach($lane in @($Entries.suite|Select-Object -Unique)){
            $rows=@($Entries|Where-Object {$_.suite -ceq $lane});$last=-1
            if($lastPassed.ContainsKey($lane)){for($i=0;$i -lt $rows.Count;$i++){if($rows[$i].chunk -ceq $lastPassed[$lane]){$last=$i;break}}}
            $lanes.Add([pscustomobject]@{suite=$lane;lastPassedChunk=$(if($last -ge 0){$rows[$last].chunk}else{$null})})
            for($offset=1;$offset -le $rows.Count;$offset++){
                $entry=$rows[($last+$offset)%$rows.Count];$key=$entry.suite+[char]0+$entry.chunk
                if($entry.priorFailure -eq $true -or ($states.ContainsKey($key) -and $states[$key] -ceq 'failed')){$failed.Add($entry)}
                elseif(-not $states.ContainsKey($key) -or $states[$key] -ceq 'unfinished'){$unfinished.Add($entry)}
            }
        }
        $ordered=@($failed.ToArray())+@($unfinished.ToArray())+@($passed.ToArray())
        $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach($entry in $ordered){if(-not $seen.Add($entry.suite+[char]0+$entry.chunk)){throw 'previous-run-not-permutation'}}
        if($ordered.Count -ne $Entries.Count -or -not $seen.SetEquals($known)){throw 'previous-run-not-permutation'}
        return [pscustomobject]@{entries=$ordered;reason='previous-run-order';lanes=$lanes.ToArray()}
    } catch { return [pscustomobject]@{entries=@($Entries);reason=$_.Exception.Message;lanes=@()} }
}

function New-NightlyDispatchSchedule {
    param([object[]]$PreparedSuites, [object[]]$PriorityMembers = @(),
        [string]$HistoryDigest = '', [object[]]$Unresolved = @(),
        [bool]$PreparationComplete = $true, [string]$RunId = '', [string]$Attempt = '',
        $PreviousRun = $null, [string]$CandidateSha = '', [datetime]$NowUtc = [datetime]::UtcNow,
        [string]$HistoryFallbackReason = '')
    $dispatchRows = [Collections.Generic.List[object]]::new()
    $dispatchKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $dispatchCanonical = [Collections.Generic.List[object]]::new()
    foreach ($dispatchSuite in $PreparedSuites) {
        foreach ($dispatchChunk in @($dispatchSuite.chunks)) {
            $dispatchKey = ([string]$dispatchSuite.id) + [char]0 + ([string]$dispatchChunk.id)
            if (-not $dispatchKeys.Add($dispatchKey)) { throw 'duplicate-canonical-chunk' }
            $dispatchPriority = $false
            foreach ($dispatchMember in $PriorityMembers) {
                if ($dispatchMember.suite -ceq $dispatchSuite.id -and @($dispatchMember.chunks) -ccontains ([string]$dispatchChunk.id)) { $dispatchPriority = $true; break }
            }
            $dispatchCanonical.Add([ordered]@{suite=[string]$dispatchSuite.id;chunk=$dispatchChunk})
            $dispatchRows.Add([pscustomobject]@{suite=[string]$dispatchSuite.id;chunk=[string]$dispatchChunk.id;canonicalOrdinal=$dispatchRows.Count;dispatchOrdinal=$dispatchRows.Count;priorFailure=$dispatchPriority;SuiteState=$dispatchSuite;ChunkRef=$dispatchChunk})
        }
    }
    # Stable partition of references. Do not repack, copy or renumber a chunk.
    $dispatchOrdered = @($dispatchRows.ToArray() | Where-Object priorFailure) + @($dispatchRows.ToArray() | Where-Object { -not $_.priorFailure })
    $previousOrder=$null
    if($null -ne $PreviousRun){
        $previousOrder=Resolve-NightlyPreviousRunOrder $dispatchRows.ToArray() $PreviousRun $CandidateSha $NowUtc
        $dispatchOrdered=@($previousOrder.entries)
    }
    $dispatchSeen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    for ($dispatchOrdinal = 0; $dispatchOrdinal -lt $dispatchOrdered.Count; $dispatchOrdinal++) {
        $dispatchRow = $dispatchOrdered[$dispatchOrdinal]
        $dispatchRow.dispatchOrdinal = $dispatchOrdinal
        if (-not $dispatchSeen.Add($dispatchRow.suite + [char]0 + $dispatchRow.chunk)) { throw 'dispatch-not-permutation' }
    }
    if ($dispatchOrdered.Count -ne $dispatchRows.Count -or -not $dispatchSeen.SetEquals($dispatchKeys)) { throw 'dispatch-not-permutation' }
    $dispatchBytes = [Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject $dispatchCanonical.ToArray() -Depth 30 -Compress))
    $dispatchHash = [Security.Cryptography.SHA256]::Create()
    try { $dispatchDigest = [BitConverter]::ToString($dispatchHash.ComputeHash($dispatchBytes)).Replace('-','').ToLowerInvariant() }
    finally { $dispatchHash.Dispose() }
    if($PreviousRun -and $PreviousRun.canonicalPlanDigest -and $PreviousRun.canonicalPlanDigest -cne $dispatchDigest){
        $dispatchOrdered=@($dispatchRows.ToArray())
        for($i=0;$i -lt $dispatchOrdered.Count;$i++){$dispatchOrdered[$i].dispatchOrdinal=$i}
        $previousOrder=[pscustomobject]@{reason='previous-run-plan-mismatch';lanes=@()}
    }
    $schedule=[pscustomobject]@{PreparedSuites=@($PreparedSuites);format='antiphon-nightly-dispatch-v1';runId=$RunId;attempt=$Attempt;historyDigest=$HistoryDigest;canonicalPlanDigest=$dispatchDigest;preparationComplete=$PreparationComplete;unresolved=@($Unresolved);entries=@($dispatchOrdered)}
    if($previousOrder){$schedule|Add-Member -NotePropertyName orderingReason -NotePropertyValue $previousOrder.reason;$schedule|Add-Member -NotePropertyName lanes -NotePropertyValue $previousOrder.lanes}
    if($HistoryFallbackReason){$schedule|Add-Member -NotePropertyName historyFallbackReason -NotePropertyValue $HistoryFallbackReason}
    return $schedule
}

function Write-NightlyDispatchSchedule {
    param($Schedule, [string]$Directory, [switch]$BestEffort)
    try {
        $dispatchDocument = [ordered]@{}
        foreach ($dispatchField in @('format','runId','attempt','historyDigest','canonicalPlanDigest','preparationComplete','unresolved')) { $dispatchDocument[$dispatchField] = $Schedule.$dispatchField }
        foreach($dispatchField in @('historyFallbackReason','orderingReason','lanes')){if($Schedule.PSObject.Properties.Name -contains $dispatchField){$dispatchDocument[$dispatchField]=$Schedule.$dispatchField}}
        $dispatchDocument['entries'] = @($Schedule.entries | Select-Object suite,chunk,canonicalOrdinal,dispatchOrdinal,priorFailure)
        Write-NightlyAtomicJson -Path (Join-Path $Directory 'dispatch-schedule.json') -Object $dispatchDocument
    } catch {
        if (-not $BestEffort) { throw }
        Write-Warning -WarningAction Continue ('dispatch-schedule-write-failed: ' + $_.Exception.Message)
        try { Write-TestPhaseFailureMarker -RunDirectory $Directory -Code 'dispatch-schedule-write-failed' } catch { }
    }
}

# Dot-source into the executor's scope, just like the three suite helpers. With no
# nonempty history the previous prepare/run/complete sequence remains lazy.
function Invoke-NightlyDispatchCoordinator {
    param([string[]]$DispatchSuites, [object[]]$DispatchPriorityMembers = @(),
        [object[]]$DispatchUnresolved = @(), [string]$DispatchHistoryDigest = '', $DispatchHistory = $null,
        $DispatchPreviousRun = $null, [string]$DispatchFallbackReason = '')
    $dispatchPrepared = [Collections.Generic.List[object]]::new()
    $dispatchBarrier = (@($DispatchPriorityMembers).Count -gt 0 -or @($DispatchUnresolved).Count -gt 0)
    if($DispatchHistory -and (@($DispatchHistory.members).Count -gt 0 -or @($DispatchHistory.unresolved).Count -gt 0)){$dispatchBarrier=$true}
    if($DispatchPreviousRun){$dispatchBarrier=$true}
    $dispatchNeedsDiscovery=$dispatchBarrier
    $dispatchSchedule = $null
    foreach ($dispatchSuiteId in $DispatchSuites) {
        if (-not $npmOk -or $lintBlocksPipeline -or -not $dotnetOk) { break }
        try { $dispatchState = . Prepare-NightlySuiteExecution -ExecutionSuiteId $dispatchSuiteId }
        catch {
            if ($dispatchBarrier) {
                $dispatchSchedule = New-NightlyDispatchSchedule -PreparedSuites $dispatchPrepared.ToArray() -PriorityMembers $DispatchPriorityMembers -Unresolved $DispatchUnresolved -HistoryDigest $DispatchHistoryDigest -PreparationComplete $false -RunId $invocationId -Attempt ([string]$Attempt)
                Write-NightlyDispatchSchedule $dispatchSchedule $LogRoot -BestEffort
            }
            throw
        }
        if ($null -eq $dispatchState) {
            if ($dispatchBarrier) {
                $dispatchSchedule = New-NightlyDispatchSchedule -PreparedSuites $dispatchPrepared.ToArray() -PriorityMembers $DispatchPriorityMembers -Unresolved $DispatchUnresolved -HistoryDigest $DispatchHistoryDigest -PreparationComplete $false -RunId $invocationId -Attempt ([string]$Attempt)
                Write-NightlyDispatchSchedule $dispatchSchedule $LogRoot -BestEffort
                throw ('dispatch-preparation-incomplete:' + $dispatchSuiteId)
            }
            continue
        }
        $dispatchPrepared.Add($dispatchState)
        if (-not $dispatchBarrier) {
            try {
                $dispatchSchedule = New-NightlyDispatchSchedule -PreparedSuites $dispatchPrepared.ToArray() -RunId $invocationId -Attempt ([string]$Attempt) -PreparationComplete $false -HistoryFallbackReason $DispatchFallbackReason
                Write-NightlyDispatchSchedule $dispatchSchedule $LogRoot -BestEffort
            } catch {
                Write-Warning -WarningAction Continue ('dispatch-schedule-write-failed: ' + $_.Exception.Message)
                try { Write-TestPhaseFailureMarker -RunDirectory $LogRoot -Code 'dispatch-schedule-write-failed' } catch { }
            }
            foreach ($dispatchChunkRef in @($dispatchState.chunks)) { . Invoke-NightlyScheduledChunk -ExecutionSuiteState $dispatchState -ExecutionChunk $dispatchChunkRef }
            . Complete-NightlySuiteExecution -CompletionSuiteState $dispatchState
        }
    }
    if (-not $dispatchBarrier) {
        try {
            $dispatchSchedule = New-NightlyDispatchSchedule -PreparedSuites $dispatchPrepared.ToArray() -RunId $invocationId -Attempt ([string]$Attempt) -HistoryFallbackReason $DispatchFallbackReason
            Write-NightlyDispatchSchedule $dispatchSchedule $LogRoot -BestEffort
        } catch {
            Write-Warning -WarningAction Continue ('dispatch-schedule-write-failed: ' + $_.Exception.Message)
            try { Write-TestPhaseFailureMarker -RunDirectory $LogRoot -Code 'dispatch-schedule-write-failed' } catch { }
        }
    }
    if ($dispatchBarrier) {
        if($DispatchHistory){
            $resolvedHistory=Resolve-NightlyPriorFailures $DispatchHistory $dispatchPrepared.ToArray() Full
            $DispatchPriorityMembers=@($resolvedHistory.members);$DispatchUnresolved=@($resolvedHistory.unresolved)
        }
        $dispatchSchedule = New-NightlyDispatchSchedule -PreparedSuites $dispatchPrepared.ToArray() -PriorityMembers $DispatchPriorityMembers -Unresolved $DispatchUnresolved -HistoryDigest $DispatchHistoryDigest -RunId $invocationId -Attempt ([string]$Attempt) -PreviousRun $DispatchPreviousRun -CandidateSha $Sha -NowUtc (Get-NightlyUtcNow)
        Write-NightlyDispatchSchedule $dispatchSchedule $LogRoot -BestEffort
        . Invoke-NightlyPreparedSchedule -PreparedSchedule $dispatchSchedule
    }
}

# Shared scheduled execution boundary; callers retain the run verdict and custody.
function Invoke-NightlyPreparedSchedule {
    param($PreparedSchedule)
    if (-not $PreparedSchedule.preparationComplete) { throw 'dispatch-preparation-incomplete' }
    $dispatchExpected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($dispatchState in $PreparedSchedule.PreparedSuites) {
        foreach ($dispatchChunk in $dispatchState.chunks) { [void]$dispatchExpected.Add($dispatchState.id + [char]0 + $dispatchChunk.id) }
    }
    $dispatchSeen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($dispatchEntry in $PreparedSchedule.entries) {
        if (-not $dispatchSeen.Add($dispatchEntry.suite + [char]0 + $dispatchEntry.chunk) -or
            $dispatchEntry.ChunkRef.id -cne $dispatchEntry.chunk -or $dispatchEntry.SuiteState.id -cne $dispatchEntry.suite) { throw 'dispatch-not-permutation' }
    }
    if (-not $dispatchSeen.SetEquals($dispatchExpected)) { throw 'dispatch-not-permutation' }
    $dispatchRemaining = @{}
    foreach ($dispatchEntry in $PreparedSchedule.entries) {
        if (-not $dispatchRemaining.ContainsKey($dispatchEntry.suite)) { $dispatchRemaining[$dispatchEntry.suite] = 0 }
        $dispatchRemaining[$dispatchEntry.suite]++
    }
    foreach ($dispatchEntry in $PreparedSchedule.entries) {
        . Invoke-NightlyScheduledChunk -ExecutionSuiteState $dispatchEntry.SuiteState -ExecutionChunk $dispatchEntry.ChunkRef
        $dispatchRemaining[$dispatchEntry.suite]--
        if ($dispatchRemaining[$dispatchEntry.suite] -eq 0) { . Complete-NightlySuiteExecution -CompletionSuiteState $dispatchEntry.SuiteState }
    }
}
