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
        foreach (var end in boundaries)
        {
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
                skipped.Add(new TaskReportSkip(end.Sequence, reason));
                continue;
            }

            if (sessionKind == AgentKind.ClaudeCode
                && TaskReportHousekeeping.IsClaudeTaskNotification(rawPrompt.Text))
            {
                var continuation = await TryClaudeContinuationAsync(
                    db, sessionId, task, raw, rawPrompt, end.Sequence, rulesIds, invoked, sessionKind, ct);
                if (continuation is null)
                {
                    skipped.Add(new TaskReportSkip(end.Sequence, "claude-notification-ack"));
                    continue;
                }

                return continuation with { Skipped = skipped };
            }

            if (TranscriptPromptSpan.IsHousekeepingPrompt(rawPrompt.Text, invoked))
            {
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
    /// eligible marked prompt. Returns <see cref="TaskReportSelection.Empty"/> when the walk
    /// hits a reply fence (the caller must not look further). Returns null to skip the ack.
    /// </summary>
    private static async Task<TaskReportSelection?> TryClaudeContinuationAsync(
        AppDbContext db,
        Guid sessionId,
        AgentTask task,
        IReadOnlyList<TranscriptPromptSpan.PromptRow> raw,
        TranscriptPromptSpan.PromptRow notification,
        long boundarySequence,
        IReadOnlySet<Guid> rulesIds,
        IReadOnlySet<string> invoked,
        AgentKind sessionKind,
        CancellationToken ct)
    {
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
            return null;

        return Finish(TaskReportSelectionKind.Selected, boundarySequence, notification, owner, raw, rulesIds, invoked, sessionKind, []);
    }

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
