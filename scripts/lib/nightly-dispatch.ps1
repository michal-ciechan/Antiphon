#requires -Version 5.1
# Diagnostic scheduling only. Canonical chunks remain the planner's own objects.
# Get-NightlyPreviousRunKind, Resolve-NightlyPreviousRunOrder and New-NightlyDispatchSchedule are compiled
# (Antiphon.Nightly/DispatchSchedule.cs): the same objects, reasons and orders, without the PowerShell bodies.
function Get-NightlyPreviousRunKind {
    param($Record)
    if (-not $script:NightlyCoreAvailable) { Assert-NightlyCoreAvailable 'Get-NightlyPreviousRunKind' }
    return [Antiphon.Nightly.DispatchSchedule]::PreviousRunKind($Record)
}

# A preference for a NEW complete attempt, never execution resume: failed and unfinished rows first, each lane
# rotated after its last observed pass. A stopped-run seed orders by its own rules (StoppedSeed in the core).
function Resolve-NightlyPreviousRunOrder {
    param([object[]]$Entries, $PreviousRun, [string]$CandidateSha, [datetime]$NowUtc)
    if (-not $script:NightlyCoreAvailable) { Assert-NightlyCoreAvailable 'Resolve-NightlyPreviousRunOrder' }
    return [Antiphon.Nightly.DispatchSchedule]::PreviousRunOrder($Entries, $PreviousRun, $CandidateSha, $NowUtc)
}

function New-NightlyDispatchSchedule {
    param([object[]]$PreparedSuites, [object[]]$PriorityMembers = @(),
        [string]$HistoryDigest = '', [object[]]$Unresolved = @(),
        [bool]$PreparationComplete = $true, [string]$RunId = '', [string]$Attempt = '',
        $PreviousRun = $null, [string]$CandidateSha = '', [datetime]$NowUtc = [datetime]::UtcNow,
        [string]$HistoryFallbackReason = '')
    if (-not $script:NightlyCoreAvailable) { Assert-NightlyCoreAvailable 'New-NightlyDispatchSchedule' }
    $build = [Antiphon.Nightly.DispatchSchedule]::Build($PreparedSuites, $PriorityMembers, $HistoryDigest, $Unresolved,
        $PreparationComplete, $RunId, $Attempt, $PreviousRun, $CandidateSha, $NowUtc, $HistoryFallbackReason)
    if ($build.Error) { throw $build.Error }
    return $build.Schedule
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
            $resolvedHistory=[Antiphon.Nightly.PriorFailureHistory]::Resolve($DispatchHistory,$dispatchPrepared.ToArray(),'Full',$null,$null)
            foreach($dispatchWarning in @($resolvedHistory.warnings)){Write-Warning -WarningAction Continue $dispatchWarning}
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
