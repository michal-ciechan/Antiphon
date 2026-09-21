using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Read-only CARD-0079 predicate. It consumes the caller's working verdict and
/// transcript facts; it does not read the database or the runner.
/// </summary>
public static class CompactionContinuationPolicy
{
    public static CompactionContinuationVerdict Evaluate(
        CompactionScopeSnapshot scope,
        IReadOnlyList<CompactionTranscriptFact> entries,
        int thresholdMinutes,
        DateTime now)
    {
        var refusal = CheckCompactionScope.Refusal(scope);
        if (refusal is not null)
            return CompactionContinuationVerdict.NotEligible(scope, refusal);

        if (thresholdMinutes == 0)
            return CompactionContinuationVerdict.NotEligible(scope, "disabled");
        if (thresholdMinutes < 10)
            return CompactionContinuationVerdict.NotEligible(scope, "threshold");

        var ordered = entries.OrderBy(e => e.Sequence).ToList();
        if (HasOrderingConflict(ordered))
            return CompactionContinuationVerdict.UnknownOrdering(scope);

        // Ends that follow a boundary disqualify that boundary. They must not become
        // the anchor, or the boundary would fall outside the open turn and a mutant
        // that ignored those ends would still look idle.
        var newestBoundarySequence = NewestOwnedBoundarySequence(ordered, scope.AcceptedStartedAt);
        var endIndex = LastEffectiveEndIndex(ordered, newestBoundarySequence);
        DateTime? endTime = endIndex >= 0 ? ordered[endIndex].EventTime ?? ordered[endIndex].CreatedAt : null;
        var open = ordered.Skip(endIndex + 1).ToList();

        var boundaryIndexes = new List<int>();
        for (var i = 0; i < open.Count; i++)
        {
            if (!TranscriptKinds.IsAutoCompactBoundary(open[i].Kind, open[i].Text))
                continue;
            if (PredatesOwnership(open[i], scope.AcceptedStartedAt, endTime))
                continue;
            boundaryIndexes.Add(i);
        }

        if (boundaryIndexes.Count == 0)
            return CompactionContinuationVerdict.NotEligible(scope, "no-boundary");

        if (SilentChainBroken(open, boundaryIndexes[0]))
            return CompactionContinuationVerdict.NotEligible(scope, "progress");

        var boundaryIndex = boundaryIndexes[^1];
        var boundary = open[boundaryIndex];
        if (!TryFindContinuation(open, boundaryIndex, out var continuation))
            return CompactionContinuationVerdict.NotEligible(scope, "missing-continuation");

        if (HasUnmatchedTool(open, boundaryIndex))
            return CompactionContinuationVerdict.NotEligible(scope, "unmatched-tool");

        if (FindOwningPrompt(open, boundaryIndex) is not { IsCorrelatedCheck: true, IsHumanOrigin: false } prompt)
            return CompactionContinuationVerdict.NotEligible(scope, "owning-prompt");

        if (!TryWaitStart(scope.AcceptedStartedAt, boundary, continuation, out var waitStart))
            return CompactionContinuationVerdict.UnknownOrdering(scope);

        var nowUtc = SpecifyUtc(now);
        if (waitStart > nowUtc)
            return CompactionContinuationVerdict.Waiting(scope, boundary, continuation, prompt, waitStart, nowUtc - waitStart);

        var elapsed = nowUtc - waitStart;
        if (elapsed < TimeSpan.FromMinutes(thresholdMinutes))
            return CompactionContinuationVerdict.Waiting(scope, boundary, continuation, prompt, waitStart, elapsed);

        return CompactionContinuationVerdict.Due(
            scope, boundary, continuation, prompt, waitStart, elapsed);
    }

    private static bool HasOrderingConflict(List<CompactionTranscriptFact> ordered)
    {
        // A higher sequence stored earlier (older CreatedAt) whose event time also runs
        // backwards is contradictory bookkeeping. Catch-up imports have a newer CreatedAt
        // and are decided by the ownership fence instead.
        for (var i = 1; i < ordered.Count; i++)
        {
            var left = ordered[i - 1];
            var right = ordered[i];
            if (left.EventTime is { } leftTime
                && right.EventTime is { } rightTime
                && right.Sequence > left.Sequence
                && rightTime < leftTime
                && right.CreatedAt < left.CreatedAt)
                return true;
        }

        return false;
    }

    private static long? NewestOwnedBoundarySequence(List<CompactionTranscriptFact> ordered, DateTime accepted)
    {
        long? sequence = null;
        foreach (var entry in ordered)
        {
            if (!TranscriptKinds.IsAutoCompactBoundary(entry.Kind, entry.Text))
                continue;
            if (PredatesOwnership(entry, accepted, null))
                continue;
            sequence = entry.Sequence;
        }

        return sequence;
    }

    private static int LastEffectiveEndIndex(List<CompactionTranscriptFact> ordered, long? beforeSequence)
    {
        var index = -1;
        for (var i = 0; i < ordered.Count; i++)
        {
            if (beforeSequence is { } limit && ordered[i].Sequence >= limit)
                break;
            if (IsEffectiveEnd(ordered[i]))
                index = i;
        }

        return index;
    }

    internal static bool IsEffectiveEnd(CompactionTranscriptFact fact) =>
        fact.Kind == TranscriptKinds.TurnEnd
        || TranscriptKinds.IsInterruptPrompt(fact.Kind, fact.Text)
        || TranscriptKinds.IsManualCompactBoundary(fact.Kind, fact.Text)
        || fact.Kind == TranscriptKinds.SessionRestartBoundary;

    private static bool PredatesOwnership(CompactionTranscriptFact fact, DateTime accepted, DateTime? endTime)
    {
        var acceptedUtc = SpecifyUtc(accepted);
        if (fact.EventTime is { } ev && SpecifyUtc(ev) < acceptedUtc)
            return true;
        if (endTime is { } end && fact.EventTime is { } ev2 && SpecifyUtc(ev2) < SpecifyUtc(end))
            return true;
        if (fact.EventTime is null && SpecifyUtc(fact.CreatedAt) < acceptedUtc)
            return true;
        return false;
    }

    private static bool SilentChainBroken(List<CompactionTranscriptFact> open, int firstBoundary)
    {
        for (var i = firstBoundary + 1; i < open.Count; i++)
        {
            var entry = open[i];
            if (IsIgnoredHousekeeping(entry))
                continue;
            if (TranscriptKinds.IsAutoCompactBoundary(entry.Kind, entry.Text))
                continue;
            if (TranscriptKinds.IsCompactionContinuationPrompt(entry.Kind, entry.Text))
                continue;
            // Assistant, thinking, tool, ordinary prompt, effective end, error and unknown.
            return true;
        }

        return false;
    }

    private static bool TryFindContinuation(
        List<CompactionTranscriptFact> open,
        int boundaryIndex,
        out CompactionTranscriptFact continuation)
    {
        for (var i = boundaryIndex + 1; i < open.Count; i++)
        {
            var entry = open[i];
            if (IsIgnoredHousekeeping(entry))
                continue;
            if (TranscriptKinds.IsCompactionContinuationPrompt(entry.Kind, entry.Text))
            {
                continuation = entry;
                return true;
            }

            continuation = default;
            return false;
        }

        continuation = default;
        return false;
    }

    private static bool HasUnmatchedTool(List<CompactionTranscriptFact> open, int boundaryIndex)
    {
        var results = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < boundaryIndex; i++)
        {
            if (open[i].Kind == TranscriptKinds.ToolResult && !string.IsNullOrEmpty(open[i].ToolUseId))
                results.Add(open[i].ToolUseId!);
        }

        for (var i = 0; i < boundaryIndex; i++)
        {
            if (open[i].Kind != TranscriptKinds.ToolCall)
                continue;
            if (string.IsNullOrEmpty(open[i].ToolUseId) || !results.Contains(open[i].ToolUseId!))
                return true;
        }

        return false;
    }

    private static CompactionTranscriptFact? FindOwningPrompt(List<CompactionTranscriptFact> open, int boundaryIndex)
    {
        for (var i = boundaryIndex - 1; i >= 0; i--)
        {
            var entry = open[i];
            if (IsIgnoredHousekeeping(entry))
                continue;
            if (TranscriptKinds.IsAutoCompactBoundary(entry.Kind, entry.Text))
                continue;
            if (TranscriptKinds.IsCompactionContinuationPrompt(entry.Kind, entry.Text))
                continue;
            if (entry.Kind == TranscriptKinds.UserPrompt)
                return entry;
            if (entry.Kind is TranscriptKinds.ToolCall or TranscriptKinds.ToolResult or TranscriptKinds.AssistantText or TranscriptKinds.Thinking)
                continue;
        }

        return null;
    }

    private static bool TryWaitStart(
        DateTime accepted,
        CompactionTranscriptFact boundary,
        CompactionTranscriptFact continuation,
        out DateTime waitStart)
    {
        var times = new List<DateTime> { SpecifyUtc(accepted), SpecifyUtc(boundary.CreatedAt), SpecifyUtc(continuation.CreatedAt) };
        if (boundary.EventTime is { } boundaryEvent)
            times.Add(SpecifyUtc(boundaryEvent));
        else if (SpecifyUtc(boundary.CreatedAt) < SpecifyUtc(accepted))
        {
            waitStart = default;
            return false;
        }

        if (continuation.EventTime is { } continuationEvent)
            times.Add(SpecifyUtc(continuationEvent));
        else if (SpecifyUtc(continuation.CreatedAt) < SpecifyUtc(accepted))
        {
            waitStart = default;
            return false;
        }

        waitStart = times.Max();
        return true;
    }

    internal static bool IsIgnoredHousekeeping(CompactionTranscriptFact fact) =>
        fact.Kind is TranscriptKinds.TurnTitle
            or TranscriptKinds.QueueEnqueue
            or TranscriptKinds.QueueDequeue
            or TranscriptKinds.QueueRemove;

    private static DateTime SpecifyUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}

/// <summary>
/// Shared D-2/D-3 action scope. Discovery, post-observation commit and queued resume
/// each call <see cref="Refusal"/> so deleting one clause fails only that guard.
/// </summary>
public static class CheckCompactionScope
{
    public static string? Refusal(CompactionScopeSnapshot scope)
    {
        if (!scope.PhysicalAlwaysOn) return "physical-always-on";
        if (!scope.LogicalOwnerAlwaysOn) return "owner-always-on";
        if (scope.PoolOwned) return "pool-owned";
        if (scope.Suspended) return "suspended";
        if (!scope.EffectiveClaudeCode) return "not-claude";
        if (!scope.CurrentStandingOwnership) return "ownership";
        if (!scope.CheckSeat) return "not-check";
        if (scope.LegacySlugOnly && !scope.PositivelyCorrelatedOwningCheck) return "slug-uncorrelated";
        if (scope.OpenNonCheckAssignment) return "open-non-check";
        if (scope.PendingCardAssignment) return "pending-card";
        if (scope.InteractiveHumanTurn) return "human-turn";
        if (scope.UnresolvedAttemptedInput) return "attempted-input";
        if (!scope.PhysicalEnabled) return "physical-disabled";
        if (!scope.OwnerEnabled) return "owner-disabled";
        if (!scope.CheckInterpretationEnabled) return "check-disabled";
        if (scope.LivenessHold) return "liveness";
        if (scope.ContinuityHold) return "continuity";
        if (scope.HerdrHold) return "herdr";
        if (scope.ProviderHold) return "provider";
        if (scope.QuotaHold) return "quota";
        if (scope.CapacityHold) return "capacity";
        if (scope.ModelHold) return "model";
        if (scope.AuthenticationRefusal) return "authentication";
        if (!scope.Running) return "not-running";
        if (!scope.Working) return "not-working";
        if (!scope.GenerationTokensEqual) return "generation";
        if (!scope.DelegationEnabled) return "delegation-disabled";
        return null;
    }
}

/// <summary>
/// D-9 allowance. Consumed at stop-request commit. A second restart needs both
/// the rolling 24 hours and a useful Check plus whole caller receipt.
/// </summary>
public static class CheckCompactionBudget
{
    public static readonly TimeSpan RollingWindow = TimeSpan.FromHours(24);

    public static bool Allows(DateTime? lastAttemptAt, bool receiptEligible, DateTime now)
    {
        if (lastAttemptAt is null)
            return true;
        if (now - lastAttemptAt.Value < RollingWindow)
            return false;
        return receiptEligible;
    }
}

public readonly record struct CompactionTranscriptFact(
    long Sequence,
    string Kind,
    string? Text,
    DateTime? EventTime,
    DateTime CreatedAt,
    string? NativeId = null,
    string? ToolUseId = null,
    bool IsCorrelatedCheck = false,
    bool IsHumanOrigin = false,
    string? StopReason = null);

public sealed record CompactionScopeSnapshot
{
    public Guid SessionId { get; init; }
    public DateTime AcceptedStartedAt { get; init; }
    public bool PhysicalAlwaysOn { get; init; }
    public bool LogicalOwnerAlwaysOn { get; init; }
    public bool PoolOwned { get; init; }
    public bool Suspended { get; init; }
    public bool EffectiveClaudeCode { get; init; }
    public bool CurrentStandingOwnership { get; init; }
    public bool CheckSeat { get; init; }
    public bool LegacySlugOnly { get; init; }
    public bool PositivelyCorrelatedOwningCheck { get; init; }
    public bool OpenNonCheckAssignment { get; init; }
    public bool PendingCardAssignment { get; init; }
    public bool InteractiveHumanTurn { get; init; }
    public bool UnresolvedAttemptedInput { get; init; }
    public bool PhysicalEnabled { get; init; }
    public bool OwnerEnabled { get; init; }
    public bool CheckInterpretationEnabled { get; init; }
    public bool LivenessHold { get; init; }
    public bool ContinuityHold { get; init; }
    public bool HerdrHold { get; init; }
    public bool ProviderHold { get; init; }
    public bool QuotaHold { get; init; }
    public bool CapacityHold { get; init; }
    public bool ModelHold { get; init; }
    public bool AuthenticationRefusal { get; init; }
    public bool Running { get; init; }
    public bool Working { get; init; }
    public bool GenerationTokensEqual { get; init; }
    public bool DelegationEnabled { get; init; }

    public static CompactionScopeSnapshot EligibleSeat(Guid sessionId, DateTime acceptedStartedAt) => new()
    {
        SessionId = sessionId,
        AcceptedStartedAt = acceptedStartedAt,
        PhysicalAlwaysOn = true,
        LogicalOwnerAlwaysOn = true,
        EffectiveClaudeCode = true,
        CurrentStandingOwnership = true,
        CheckSeat = true,
        PositivelyCorrelatedOwningCheck = true,
        PhysicalEnabled = true,
        OwnerEnabled = true,
        CheckInterpretationEnabled = true,
        Running = true,
        Working = true,
        GenerationTokensEqual = true,
        DelegationEnabled = true,
    };
}

public sealed record CompactionContinuationVerdict(
    bool Eligible,
    bool Overdue,
    bool Unknown,
    string Reason,
    Guid? SessionId,
    DateTime? AcceptedStartedAt,
    long? OrdinaryPromptSequence,
    long? BoundarySequence,
    long? ContinuationSequence,
    string? NativeBoundaryIdentity,
    string? NativeContinuationIdentity,
    DateTime? WaitStart,
    TimeSpan? Elapsed)
{
    public static CompactionContinuationVerdict NotEligible(CompactionScopeSnapshot scope, string reason) =>
        new(false, false, false, reason, scope.SessionId, scope.AcceptedStartedAt,
            null, null, null, null, null, null, null);

    public static CompactionContinuationVerdict UnknownOrdering(CompactionScopeSnapshot scope) =>
        new(false, false, true, "unknown-ordering", scope.SessionId, scope.AcceptedStartedAt,
            null, null, null, null, null, null, null);

    public static CompactionContinuationVerdict Waiting(
        CompactionScopeSnapshot scope,
        CompactionTranscriptFact boundary,
        CompactionTranscriptFact continuation,
        CompactionTranscriptFact prompt,
        DateTime waitStart,
        TimeSpan elapsed) =>
        Build(false, false, scope, boundary, continuation, prompt, waitStart, elapsed, "waiting");

    public static CompactionContinuationVerdict Due(
        CompactionScopeSnapshot scope,
        CompactionTranscriptFact boundary,
        CompactionTranscriptFact continuation,
        CompactionTranscriptFact prompt,
        DateTime waitStart,
        TimeSpan elapsed) =>
        Build(true, true, scope, boundary, continuation, prompt, waitStart, elapsed, "overdue");

    private static CompactionContinuationVerdict Build(
        bool eligible,
        bool overdue,
        CompactionScopeSnapshot scope,
        CompactionTranscriptFact boundary,
        CompactionTranscriptFact continuation,
        CompactionTranscriptFact prompt,
        DateTime waitStart,
        TimeSpan elapsed,
        string reason) =>
        new(eligible, overdue, false, reason, scope.SessionId, scope.AcceptedStartedAt,
            prompt.Sequence, boundary.Sequence, continuation.Sequence,
            boundary.NativeId, continuation.NativeId, waitStart, elapsed);
}
