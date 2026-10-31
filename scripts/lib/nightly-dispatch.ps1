#requires -Version 5.1
# Diagnostic scheduling only. Canonical chunks remain the planner's own objects.
function Resolve-NightlyPreviousRunOrder {
    param([object[]]$Entries, $PreviousRun, [string]$CandidateSha, [datetime]$NowUtc)
    return [pscustomobject]@{entries=@($Entries);reason='original-order';lanes=@()}
}

function New-NightlyDispatchSchedule {
    param([object[]]$PreparedSuites, [object[]]$PriorityMembers = @(),
        [string]$HistoryDigest = '', [object[]]$Unresolved = @(),
        [bool]$PreparationComplete = $true, [string]$RunId = '', [string]$Attempt = '')
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
    return [pscustomobject]@{PreparedSuites=@($PreparedSuites);format='antiphon-nightly-dispatch-v1';runId=$RunId;attempt=$Attempt;historyDigest=$HistoryDigest;canonicalPlanDigest=$dispatchDigest;preparationComplete=$PreparationComplete;unresolved=@($Unresolved);entries=@($dispatchOrdered)}
}

function Write-NightlyDispatchSchedule {
    param($Schedule, [string]$Directory, [switch]$BestEffort)
    try {
        $dispatchDocument = [ordered]@{}
        foreach ($dispatchField in @('format','runId','attempt','historyDigest','canonicalPlanDigest','preparationComplete','unresolved')) { $dispatchDocument[$dispatchField] = $Schedule.$dispatchField }
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
        [object[]]$DispatchUnresolved = @(), [string]$DispatchHistoryDigest = '')
    $dispatchPrepared = [Collections.Generic.List[object]]::new()
    $dispatchBarrier = (@($DispatchPriorityMembers).Count -gt 0 -or @($DispatchUnresolved).Count -gt 0)
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
                $dispatchSchedule = New-NightlyDispatchSchedule -PreparedSuites $dispatchPrepared.ToArray() -RunId $invocationId -Attempt ([string]$Attempt) -PreparationComplete $false
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
            $dispatchSchedule = New-NightlyDispatchSchedule -PreparedSuites $dispatchPrepared.ToArray() -RunId $invocationId -Attempt ([string]$Attempt)
            Write-NightlyDispatchSchedule $dispatchSchedule $LogRoot -BestEffort
        } catch {
            Write-Warning -WarningAction Continue ('dispatch-schedule-write-failed: ' + $_.Exception.Message)
            try { Write-TestPhaseFailureMarker -RunDirectory $LogRoot -Code 'dispatch-schedule-write-failed' } catch { }
        }
    }
    if ($dispatchBarrier) {
        $dispatchSchedule = New-NightlyDispatchSchedule -PreparedSuites $dispatchPrepared.ToArray() -PriorityMembers $DispatchPriorityMembers -Unresolved $DispatchUnresolved -HistoryDigest $DispatchHistoryDigest -RunId $invocationId -Attempt ([string]$Attempt)
        Write-NightlyDispatchSchedule $dispatchSchedule $LogRoot
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
