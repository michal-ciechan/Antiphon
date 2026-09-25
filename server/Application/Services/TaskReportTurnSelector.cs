using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

internal enum TaskReportSelectionKind
{
    None = 0,
    Selected = 1,
    Uncorrelated = 2,
    PreReply = 3,
    Interrupted = 4,
}

internal enum DeliveryWatchdogAttribution
{
    Settled = 0,
    StillUncorrelated = 1,
    DeferredOrNoReport = 2,
    NoLongerOpen = 3,
    Indeterminate = 4,
}

/// <summary>One skipped boundary. <see cref="Reason"/> is a short code, never prompt text.</summary>
internal readonly record struct TaskReportSkip(long Sequence, string Reason);

/// <summary>
/// The boundary and prompts chosen for one task (CARD-0714). Sequences are exclusive caps:
/// a null cap means the window runs to the end of the session.
/// </summary>
internal sealed record TaskReportSelection(
    TaskReportSelectionKind Kind,
    long? BoundarySequence,
    long? RawPromptSequence,
    long? EffectiveOwnerSequence,
    long? NextRawPromptSequence,
    long? NextRealPromptSequence,
    IReadOnlyList<TaskReportSkip> Skipped)
{
    public static TaskReportSelection Empty { get; } = new(
        TaskReportSelectionKind.None, null, null, null, null, null, []);
}

/// <summary>
/// Picks the newest attributable turn inside the current task's ownership span.
/// Housekeeping boundaries are skipped. A real unmarked prompt is a barrier.
/// Performs no settlement.
/// </summary>
internal static class TaskReportTurnSelector
{
    internal static async Task<TaskReportSelection> SelectAsync(
        AppDbContext db, Guid sessionId, AgentTask task, CancellationToken ct)
    {
        var sessionKind = await db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => (AgentKind?)s.AgentKind)
            .FirstOrDefaultAsync(ct) ?? AgentKind.Raw;

        var span = await TranscriptPromptSpan.LoadAsync(db, sessionId, task.DispatchedAt, ct);
        var raw = span.RawPrompts;
        // A backed rules header is housekeeping for every provider. The completion
        // envelope stays Grok-only inside the allowlist.
        var rulesIds = (await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == sessionId
                && m.Origin == QueuedMessageOrigin.System
                && m.RulesRefreshKey != null)
            .Select(m => m.Id)
            .ToListAsync(ct)).ToHashSet();

        var invoked = raw
            .Where(r => task.DispatchedAt is null || r.Timestamp is null || r.Timestamp > task.DispatchedAt)
            .Select(r => TranscriptKinds.TryReadLocalCommandName(TranscriptKinds.UserPrompt, r.Text))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        var boundaries = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.TurnEnd)
            .OrderByDescending(t => t.Sequence)
            .Select(t => new BoundaryRow(t.Sequence, t.Kind, t.StopReason))
            .ToListAsync(ct);

        var skipped = new List<TaskReportSkip>();
        for (var index = 0; index < boundaries.Count; index++)
        {
            var end = boundaries[index];
            var olderEnd = index + 1 < boundaries.Count ? boundaries[index + 1].Sequence : (long?)null;
            var rawPrompt = LastBefore(raw, end.Sequence);
            if (rawPrompt is null)
            {
                skipped.Add(new TaskReportSkip(end.Sequence, "promptless"));
                continue;
            }

            if (TaskReportHousekeeping.IsAllowlistedPrompt(sessionKind, rawPrompt.Text, rulesIds))
            {
                var reason = TaskReportHousekeeping.IsMeasuredCompletionEnvelope(rawPrompt.Text)
                    ? "grok-background-completion"
                    : "grok-rules-refresh";
                var owned = RealOwnerOfSkippedTurn(
                    end, olderEnd, rawPrompt, task, raw, rulesIds, invoked, sessionKind);
                if (owned is not null)
                    return owned with { Skipped = skipped };
                skipped.Add(new TaskReportSkip(end.Sequence, reason));
                continue;
            }

            if (sessionKind == AgentKind.ClaudeCode
                && TaskReportHousekeeping.IsClaudeTaskNotification(rawPrompt.Text))
            {
                var continuation = await TryClaudeContinuationAsync(
                    db, sessionId, task, raw, rawPrompt, end.Sequence, olderEnd, end.StopReason, rulesIds, invoked, sessionKind, ct);
                if (continuation is null)
                {
                    skipped.Add(new TaskReportSkip(end.Sequence, "claude-notification-ack"));
                    continue;
                }

                return continuation with { Skipped = skipped };
            }

            if (TranscriptPromptSpan.IsHousekeepingPrompt(rawPrompt.Text, invoked))
            {
                var owned = RealOwnerOfSkippedTurn(
                    end, olderEnd, rawPrompt, task, raw, rulesIds, invoked, sessionKind);
                if (owned is not null)
                    return owned with { Skipped = skipped };
                skipped.Add(new TaskReportSkip(end.Sequence, "inert-housekeeping"));
                continue;
            }

            if (!IsTimeEligible(rawPrompt, task.DispatchedAt))
            {
                skipped.Add(new TaskReportSkip(end.Sequence, "pre-dispatch"));
                continue;
            }

            if (task.RepliedAtSequence is long watermark && rawPrompt.Sequence <= watermark)
            {
                return Finish(TaskReportSelectionKind.PreReply, end.Sequence, rawPrompt, rawPrompt, raw, rulesIds, invoked, sessionKind, skipped);
            }

            if (!TranscriptKinds.IsReportBoundary(end.Kind, end.StopReason))
            {
                return Finish(TaskReportSelectionKind.Interrupted, end.Sequence, rawPrompt, rawPrompt, raw, rulesIds, invoked, sessionKind, skipped);
            }

            if (rawPrompt.Text is not string promptText
                || !promptText.Contains(DelegationReportFormatter.TaskMarker(task.Id), StringComparison.Ordinal))
            {
                return Finish(TaskReportSelectionKind.Uncorrelated, end.Sequence, rawPrompt, rawPrompt, raw, rulesIds, invoked, sessionKind, skipped);
            }

            return Finish(TaskReportSelectionKind.Selected, end.Sequence, rawPrompt, rawPrompt, raw, rulesIds, invoked, sessionKind, skipped);
        }

        return new TaskReportSelection(TaskReportSelectionKind.None, null, null, null, null, null, skipped);
    }

    internal static async Task<List<string>> LoadResponseTextsAsync(
        AppDbContext db, Guid sessionId, TaskReportSelection selection, string? apiCallId, CancellationToken ct)
    {
        if (selection.RawPromptSequence is not long rawSequence)
            return [];

        var after = apiCallId is null
            ? rawSequence
            : selection.EffectiveOwnerSequence ?? rawSequence;
        var before = apiCallId is null
            ? selection.NextRawPromptSequence
            : selection.NextRealPromptSequence;

        var query = db.TranscriptEntries.AsNoTracking().Where(t =>
            t.AgentSessionId == sessionId
            && t.Kind == TranscriptKinds.AssistantText
            && t.Text != null
            && t.Sequence > after);
        if (before is long cap)
            query = query.Where(t => t.Sequence < cap);
        if (apiCallId is not null)
            query = query.Where(t => t.ApiCallId == apiCallId);

        return await query.OrderBy(t => t.Sequence).Select(t => t.Text!).ToListAsync(ct);
    }

    /// <summary>
    /// A notification response with this task's closing token, whose walk back reaches an
    /// eligible marked prompt. An eligible unmarked owner is a barrier
    /// (<see cref="TaskReportSelectionKind.Uncorrelated"/>), including when the answer has no
    /// closing token. Returns null only to skip an acknowledgement that has no real owner in
    /// its turn.
    /// </summary>
    private static async Task<TaskReportSelection?> TryClaudeContinuationAsync(
        AppDbContext db,
        Guid sessionId,
        AgentTask task,
        IReadOnlyList<TranscriptPromptSpan.PromptRow> raw,
        TranscriptPromptSpan.PromptRow notification,
        long boundarySequence,
        long? olderEndSequence,
        string? stopReason,
        IReadOnlySet<Guid> rulesIds,
        IReadOnlySet<string> invoked,
        AgentKind sessionKind,
        CancellationToken ct)
    {
        // Judge the in-turn owner before the closing-token check. An answer with no token
        // used to return null here, and the caller then walked back across the unmarked prompt.
        var inTurn = NewestRealBetween(raw, olderEndSequence, boundarySequence, sessionKind, rulesIds, invoked);
        if (OwnRealPrompt(inTurn, notification, boundarySequence, stopReason, task, raw, rulesIds, invoked, sessionKind) is { } inTurnOwned)
            return inTurnOwned;

        var nextRaw = NextSequence(raw, notification.Sequence, static _ => true);
        var texts = db.TranscriptEntries.AsNoTracking().Where(t =>
            t.AgentSessionId == sessionId
            && t.Kind == TranscriptKinds.AssistantText
            && t.Text != null
            && t.Sequence > notification.Sequence);
        if (nextRaw is long cap)
            texts = texts.Where(t => t.Sequence < cap);
        var bodies = await texts.Select(t => t.Text!).ToListAsync(ct);
        if (!bodies.Any(text => DelegationReportFormatter.TryFindReportToken(task.Id, text, out _)))
            return null;

        TranscriptPromptSpan.PromptRow? owner = null;
        for (var i = raw.Count - 1; i >= 0; i--)
        {
            var row = raw[i];
            if (row.Sequence >= notification.Sequence)
                continue;
            if (IsHousekeepingRow(sessionKind, row, rulesIds, invoked))
                continue;
            if (!IsTurnOwningKind(row.Kind))
                continue;
            owner = row;
            break;
        }

        if (owner is null)
            return null;
        if (!IsTimeEligible(owner, task.DispatchedAt))
            return null;
        if (task.RepliedAtSequence is long watermark && owner.Sequence <= watermark)
        {
            return Finish(TaskReportSelectionKind.PreReply, boundarySequence, notification, owner, raw, rulesIds, invoked, sessionKind, []);
        }

        if (owner.Text is not string ownerText
            || !ownerText.Contains(DelegationReportFormatter.TaskMarker(task.Id), StringComparison.Ordinal))
        {
            return Finish(TaskReportSelectionKind.Uncorrelated, boundarySequence, notification, owner, raw, rulesIds, invoked, sessionKind, []);
        }

        return Finish(TaskReportSelectionKind.Selected, boundarySequence, notification, owner, raw, rulesIds, invoked, sessionKind, []);
    }

    /// <summary>
    /// The newest real prompt between the next-older turn end and this housekeeping boundary
    /// owns the turn. Null means there is no eligible owner, so the caller may keep walking.
    /// </summary>
    private static TaskReportSelection? RealOwnerOfSkippedTurn(
        BoundaryRow end,
        long? olderEndSequence,
        TranscriptPromptSpan.PromptRow boundaryPrompt,
        AgentTask task,
        IReadOnlyList<TranscriptPromptSpan.PromptRow> raw,
        IReadOnlySet<Guid> rulesIds,
        IReadOnlySet<string> invoked,
        AgentKind sessionKind)
    {
        var owner = NewestRealBetween(raw, olderEndSequence, end.Sequence, sessionKind, rulesIds, invoked);
        return OwnRealPrompt(owner, boundaryPrompt, end.Sequence, end.StopReason, task, raw, rulesIds, invoked, sessionKind);
    }

    /// <summary>
    /// Dispatch-time, reply-watermark and marker checks for a real prompt that shares a
    /// skipped housekeeping turn. An unmarked eligible owner is <see cref="TaskReportSelectionKind.Uncorrelated"/>.
    /// </summary>
    private static TaskReportSelection? OwnRealPrompt(
        TranscriptPromptSpan.PromptRow? owner,
        TranscriptPromptSpan.PromptRow boundaryPrompt,
        long boundarySequence,
        string? stopReason,
        AgentTask task,
        IReadOnlyList<TranscriptPromptSpan.PromptRow> raw,
        IReadOnlySet<Guid> rulesIds,
        IReadOnlySet<string> invoked,
        AgentKind sessionKind)
    {
        if (owner is null || !IsTimeEligible(owner, task.DispatchedAt))
            return null;
        if (task.RepliedAtSequence is long watermark && owner.Sequence <= watermark)
        {
            return Finish(TaskReportSelectionKind.PreReply, boundarySequence, boundaryPrompt, owner, raw, rulesIds, invoked, sessionKind, []);
        }

        if (stopReason is not null && !TranscriptKinds.IsReportBoundary(TranscriptKinds.TurnEnd, stopReason))
        {
            return Finish(TaskReportSelectionKind.Interrupted, boundarySequence, boundaryPrompt, owner, raw, rulesIds, invoked, sessionKind, []);
        }

        if (owner.Text is not string text
            || !text.Contains(DelegationReportFormatter.TaskMarker(task.Id), StringComparison.Ordinal))
        {
            return Finish(TaskReportSelectionKind.Uncorrelated, boundarySequence, boundaryPrompt, owner, raw, rulesIds, invoked, sessionKind, []);
        }

        // A marked owner does not take the housekeeping boundary. Its own earlier TurnEnd
        // is selected when the walk reaches it. Claiming this boundary would publish the
        // acknowledgement, or nudge a brief that has not finished a turn of its own.
        return null;
    }

    /// <summary>
    /// Newest UserPrompt or QueuedUserPrompt strictly after <paramref name="afterSequence"/>
    /// and strictly before <paramref name="beforeSequence"/> that is not housekeeping.
    /// Either kind owns the turn.
    /// </summary>
    private static TranscriptPromptSpan.PromptRow? NewestRealBetween(
        IReadOnlyList<TranscriptPromptSpan.PromptRow> rows,
        long? afterSequence,
        long beforeSequence,
        AgentKind sessionKind,
        IReadOnlySet<Guid> rulesIds,
        IReadOnlySet<string> invoked)
    {
        TranscriptPromptSpan.PromptRow? newest = null;
        foreach (var row in rows)
        {
            if (afterSequence is long floor && row.Sequence <= floor)
                continue;
            if (row.Sequence >= beforeSequence)
                break;
            if (!IsTurnOwningKind(row.Kind) || IsHousekeepingRow(sessionKind, row, rulesIds, invoked))
                continue;
            newest = row;
        }

        return newest;
    }

    private static bool IsTurnOwningKind(string kind) =>
        kind == TranscriptKinds.UserPrompt || kind == TranscriptKinds.QueuedUserPrompt;

    private static bool IsHousekeepingRow(
        AgentKind sessionKind,
        TranscriptPromptSpan.PromptRow row,
        IReadOnlySet<Guid> rulesIds,
        IReadOnlySet<string> invoked) =>
        TaskReportHousekeeping.IsAllowlistedPrompt(sessionKind, row.Text, rulesIds)
        || TranscriptPromptSpan.IsHousekeepingPrompt(row.Text, invoked);

    private static bool IsTimeEligible(TranscriptPromptSpan.PromptRow row, DateTime? dispatchedAt) =>
        dispatchedAt is null || row.Timestamp is null || row.Timestamp > dispatchedAt;

    private static TaskReportSelection Finish(
        TaskReportSelectionKind kind,
        long boundarySequence,
        TranscriptPromptSpan.PromptRow rawPrompt,
        TranscriptPromptSpan.PromptRow owner,
        IReadOnlyList<TranscriptPromptSpan.PromptRow> raw,
        IReadOnlySet<Guid> rulesIds,
        IReadOnlySet<string> invoked,
        AgentKind sessionKind,
        IReadOnlyList<TaskReportSkip> skipped)
    {
        var real = raw.Where(row => !IsHousekeepingRow(sessionKind, row, rulesIds, invoked)).ToList();
        return new TaskReportSelection(
            kind,
            boundarySequence,
            rawPrompt.Sequence,
            owner.Sequence,
            NextSequence(raw, rawPrompt.Sequence, static _ => true),
            NextSequence(real, owner.Sequence, static _ => true),
            skipped);
    }

    private static long? NextSequence(
        IReadOnlyList<TranscriptPromptSpan.PromptRow> rows,
        long after,
        Func<TranscriptPromptSpan.PromptRow, bool> include)
    {
        foreach (var row in rows)
        {
            if (row.Sequence > after && include(row))
                return row.Sequence;
        }

        return null;
    }

    private static TranscriptPromptSpan.PromptRow? LastBefore(
        IReadOnlyList<TranscriptPromptSpan.PromptRow> rows, long sequence)
    {
        TranscriptPromptSpan.PromptRow? owner = null;
        foreach (var row in rows)
        {
            if (row.Sequence >= sequence)
                break;
            owner = row;
        }

        return owner;
    }

    private readonly record struct BoundaryRow(long Sequence, string? Kind, string? StopReason);
}
