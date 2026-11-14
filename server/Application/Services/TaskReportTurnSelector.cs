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
/// A Grok allowlisted boundary waits for the marked brief's own turn end. An inert
/// compaction or local-command boundary settles that brief on this turn end, because
/// those records sit inside the brief's turn. A real unmarked prompt is a barrier.
/// Performs no settlement.
/// </summary>
internal static class TaskReportTurnSelector
{
    internal static async Task<TaskReportSelection> SelectAsync(
        AppDbContext db, Guid sessionId, AgentTask task, CancellationToken ct, long? boundaryCeiling = null)
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

        var realPrompts = CreateRealPromptIndex(raw, sessionKind, rulesIds, invoked);

        var boundaries = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.TurnEnd
                && (boundaryCeiling == null || t.Sequence <= boundaryCeiling))
            .OrderByDescending(t => t.Sequence)
            .Select(t => new BoundaryRow(t.Sequence, t.Kind, t.StopReason, t.ApiCallId))
            .ToListAsync(ct);

        // Stored tool rows only. A tool that has not been ingested yet is not proof.
        // CARD-1821: an end_turn is the only sibling phantom, and a tool at or before
        // the first eligible prompt is outside every owner span. The sequence floor
        // keeps the (AgentSessionId, Kind, Sequence) index.
        var toolRows = await LoadSiblingToolsAsync(db, sessionId, raw, boundaries, task.DispatchedAt, ct);

        var skipped = new List<TaskReportSkip>();
        TranscriptPromptSpan.PromptRow? phantomBarrier = null;
        long phantomBarrierBoundary = 0;
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

            if (phantomBarrier is not null && rawPrompt.Sequence < phantomBarrier.Sequence)
            {
                return SelectionBehindPhantomBarrier(
                    phantomBarrier, phantomBarrierBoundary, task, raw, realPrompts, skipped);
            }

            if (TaskReportHousekeeping.IsAllowlistedPrompt(sessionKind, rawPrompt.Text, rulesIds))
            {
                var reason = TaskReportHousekeeping.IsMeasuredCompletionEnvelope(rawPrompt.Text)
                    ? "grok-background-completion"
                    : "grok-rules-refresh";
                var owned = RealOwnerOfSkippedTurn(
                    end, olderEnd, rawPrompt, task, raw, realPrompts, settleMarkedOwner: false);
                if (owned is not null)
                    return owned with { Skipped = skipped };
                skipped.Add(new TaskReportSkip(end.Sequence, reason));
                continue;
            }

            if (sessionKind == AgentKind.ClaudeCode
                && TaskReportHousekeeping.IsClaudeTaskNotification(rawPrompt.Text))
            {
                var continuation = await TryClaudeContinuationAsync(
                    db, sessionId, task, raw, rawPrompt, end.Sequence, olderEnd, end.StopReason, realPrompts, ct);
                if (continuation is null)
                {
                    skipped.Add(new TaskReportSkip(end.Sequence, "claude-notification-ack"));
                    continue;
                }

                // CARD-1821: a notification answer that shares its end_turn with a stored
                // tool has not finished. The span is the walked-back owner through the
                // next real prompt. Uncorrelated and interrupted results stay as they are.
                if (continuation.Kind == TaskReportSelectionKind.Selected
                    && continuation.EffectiveOwnerSequence is long notificationOwner
                    && IsEndTurnSiblingPhantom(end, notificationOwner, continuation.NextRealPromptSequence, toolRows))
                {
                    skipped.Add(new TaskReportSkip(end.Sequence, "sibling-tool"));
                    // The unfinished owner blocks older boundaries, same as a real prompt.
                    RememberPhantomBarrier(
                        raw, notificationOwner, end.Sequence, ref phantomBarrier, ref phantomBarrierBoundary);
                    continue;
                }

                return continuation with { Skipped = skipped };
            }

            if (TranscriptPromptSpan.IsHousekeepingPrompt(rawPrompt.Text, invoked))
            {
                // Compaction and local-command records share the brief's turn. The
                // TurnEnd belongs to that brief, including when the only compaction
                // record is the continuation prompt. A sibling phantom is not a
                // finished turn, so it does not hide that brief from a newer boundary.
                var floor = PhantomTransparentFloor(
                    boundaries, index, realPrompts, toolRows, task);
                var owned = RealOwnerOfSkippedTurn(
                    end, floor, rawPrompt, task, raw, realPrompts, settleMarkedOwner: true);
                if (owned is not null)
                {
                    // CARD-1821: compaction and local-command records share the brief's turn,
                    // but an end_turn that shares its ApiCallId with a stored tool in that
                    // span is still a sibling phantom. max_tokens and cancelled stay on the
                    // owned result. The tool span is the owner through the next real prompt.
                    if (owned.Kind == TaskReportSelectionKind.Selected
                        && owned.EffectiveOwnerSequence is long housekeepingOwner
                        && IsEndTurnSiblingPhantom(end, housekeepingOwner, owned.NextRealPromptSequence, toolRows))
                    {
                        skipped.Add(new TaskReportSkip(end.Sequence, "sibling-tool"));
                        RememberPhantomBarrier(
                            raw, housekeepingOwner, end.Sequence, ref phantomBarrier, ref phantomBarrierBoundary);
                        continue;
                    }

                    return owned with { Skipped = skipped };
                }

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
                return Finish(TaskReportSelectionKind.PreReply, end.Sequence, rawPrompt, rawPrompt, raw, realPrompts, skipped);
            }

            if (!TranscriptKinds.IsReportBoundary(end.Kind, end.StopReason))
            {
                return Finish(TaskReportSelectionKind.Interrupted, end.Sequence, rawPrompt, rawPrompt, raw, realPrompts, skipped);
            }

            // CARD-1473: an end_turn that shares its ApiCallId with a ToolCall in this
            // prompt span is a sibling phantom, not a finished report. max_tokens and
            // other stop reasons stay terminal. A different response id does not count,
            // and a newer real prompt remains the attribution barrier.
            var nextReal = realPrompts.NextSequence(rawPrompt.Sequence);
            if (IsEndTurnSiblingPhantom(end, rawPrompt.Sequence, nextReal, toolRows))
            {
                skipped.Add(new TaskReportSkip(end.Sequence, "sibling-tool"));
                if (phantomBarrier is null)
                {
                    phantomBarrier = rawPrompt;
                    phantomBarrierBoundary = end.Sequence;
                }

                continue;
            }

            if (rawPrompt.Text is not string promptText
                || !promptText.Contains(DelegationReportFormatter.TaskMarker(task.Id), StringComparison.Ordinal))
            {
                return Finish(TaskReportSelectionKind.Uncorrelated, end.Sequence, rawPrompt, rawPrompt, raw, realPrompts, skipped);
            }

            return Finish(TaskReportSelectionKind.Selected, end.Sequence, rawPrompt, rawPrompt, raw, realPrompts, skipped);
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
        RealPromptIndex realPrompts,
        CancellationToken ct)
    {
        // Judge the in-turn owner before the closing-token check. An answer with no token
        // used to return null here, and the caller then walked back across the unmarked prompt.
        var inTurn = realPrompts.NewestBetween(olderEndSequence, boundarySequence);
        if (OwnRealPrompt(inTurn, notification, boundarySequence, stopReason, task, raw, realPrompts, settleMarkedOwner: false) is { } inTurnOwned)
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

        var owner = realPrompts.NewestBetween(null, notification.Sequence);

        if (owner is null)
            return null;
        if (!IsTimeEligible(owner, task.DispatchedAt))
            return null;
        if (task.RepliedAtSequence is long watermark && owner.Sequence <= watermark)
        {
            return Finish(TaskReportSelectionKind.PreReply, boundarySequence, notification, owner, raw, realPrompts, []);
        }

        if (owner.Text is not string ownerText
            || !ownerText.Contains(DelegationReportFormatter.TaskMarker(task.Id), StringComparison.Ordinal))
        {
            return Finish(TaskReportSelectionKind.Uncorrelated, boundarySequence, notification, owner, raw, realPrompts, []);
        }

        return Finish(TaskReportSelectionKind.Selected, boundarySequence, notification, owner, raw, realPrompts, []);
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
        RealPromptIndex realPrompts,
        bool settleMarkedOwner)
    {
        var owner = realPrompts.NewestBetween(olderEndSequence, end.Sequence);
        return OwnRealPrompt(
            owner, boundaryPrompt, end.Sequence, end.StopReason, task, raw, realPrompts, settleMarkedOwner);
    }

    /// <summary>
    /// Dispatch-time, reply-watermark and marker checks for a real prompt that shares a
    /// skipped housekeeping turn. An unmarked eligible owner is <see cref="TaskReportSelectionKind.Uncorrelated"/>.
    /// A marked owner settles on this boundary only when <paramref name="settleMarkedOwner"/>
    /// is set (inert compaction and local commands). A Grok reminder waits for the brief's own turn end.
    /// </summary>
    private static TaskReportSelection? OwnRealPrompt(
        TranscriptPromptSpan.PromptRow? owner,
        TranscriptPromptSpan.PromptRow boundaryPrompt,
        long boundarySequence,
        string? stopReason,
        AgentTask task,
        IReadOnlyList<TranscriptPromptSpan.PromptRow> raw,
        RealPromptIndex realPrompts,
        bool settleMarkedOwner)
    {
        if (owner is null || !IsTimeEligible(owner, task.DispatchedAt))
            return null;
        if (task.RepliedAtSequence is long watermark && owner.Sequence <= watermark)
        {
            return Finish(TaskReportSelectionKind.PreReply, boundarySequence, boundaryPrompt, owner, raw, realPrompts, []);
        }

        if (stopReason is not null && !TranscriptKinds.IsReportBoundary(TranscriptKinds.TurnEnd, stopReason))
        {
            return Finish(TaskReportSelectionKind.Interrupted, boundarySequence, boundaryPrompt, owner, raw, realPrompts, []);
        }

        if (owner.Text is not string text
            || !text.Contains(DelegationReportFormatter.TaskMarker(task.Id), StringComparison.Ordinal))
        {
            return Finish(TaskReportSelectionKind.Uncorrelated, boundarySequence, boundaryPrompt, owner, raw, realPrompts, []);
        }

        // A Grok completion reminder or rules refresh is its own turn. Publishing that
        // boundary would take the acknowledgement as the report, or nudge a brief that
        // has not finished. Compaction and local-command records have no separate turn
        // end, so the caller settles the marked brief on this boundary instead.
        if (!settleMarkedOwner)
            return null;

        return Finish(TaskReportSelectionKind.Selected, boundarySequence, boundaryPrompt, owner, raw, realPrompts, []);
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
        RealPromptIndex realPrompts,
        IReadOnlyList<TaskReportSkip> skipped)
    {
        return new TaskReportSelection(
            kind,
            boundarySequence,
            rawPrompt.Sequence,
            owner.Sequence,
            NextSequence(raw, rawPrompt.Sequence, static _ => true),
            realPrompts.NextSequence(owner.Sequence),
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

    /// <summary>
    /// A newer real prompt whose <c>end_turn</c> was a sibling phantom still blocks
    /// older boundaries. A marked brief in that position has not finished.
    /// </summary>
    private static TaskReportSelection SelectionBehindPhantomBarrier(
        TranscriptPromptSpan.PromptRow blocker,
        long boundarySequence,
        AgentTask task,
        IReadOnlyList<TranscriptPromptSpan.PromptRow> raw,
        RealPromptIndex realPrompts,
        List<TaskReportSkip> skipped)
    {
        if (task.RepliedAtSequence is long watermark && blocker.Sequence <= watermark)
        {
            return Finish(TaskReportSelectionKind.PreReply, boundarySequence, blocker, blocker, raw, realPrompts, skipped);
        }

        if (blocker.Text is not string text
            || !text.Contains(DelegationReportFormatter.TaskMarker(task.Id), StringComparison.Ordinal))
        {
            return Finish(TaskReportSelectionKind.Uncorrelated, boundarySequence, blocker, blocker, raw, realPrompts, skipped);
        }

        return new TaskReportSelection(TaskReportSelectionKind.None, null, null, null, null, null, skipped);
    }

    /// <summary>
    /// The newest older TurnEnd that still closes ownership. An <c>end_turn</c> that
    /// this span would skip as a sibling phantom is transparent, so a newer housekeeping
    /// boundary keeps the marked brief behind it. <c>max_tokens</c> and <c>cancelled</c>
    /// stay opaque.
    /// </summary>
    internal static long? PhantomTransparentFloor(
        IReadOnlyList<BoundaryRow> boundaries,
        int index,
        RealPromptIndex realPrompts,
        IReadOnlyList<ToolSiblingRow> tools,
        AgentTask task)
    {
        long? floor = null;
        for (var i = boundaries.Count - 1; i > index; i--)
        {
            var candidate = boundaries[i];
            if (IsTransparentSiblingPhantom(candidate, floor, realPrompts, tools, task))
                continue;
            floor = candidate.Sequence;
        }

        return floor;
    }

    private static bool IsTransparentSiblingPhantom(
        BoundaryRow candidate,
        long? floor,
        RealPromptIndex realPrompts,
        IReadOnlyList<ToolSiblingRow> tools,
        AgentTask task)
    {
        if (!string.Equals(candidate.StopReason, TranscriptKinds.StopReasons.EndTurn, StringComparison.Ordinal))
            return false;
        if (string.IsNullOrEmpty(candidate.ApiCallId))
            return false;

        var owner = realPrompts.NewestBetween(floor, candidate.Sequence);
        if (owner is null || !IsTimeEligible(owner, task.DispatchedAt))
            return false;
        if (task.RepliedAtSequence is long watermark && owner.Sequence <= watermark)
            return false;
        if (owner.Text is not string text
            || !text.Contains(DelegationReportFormatter.TaskMarker(task.Id), StringComparison.Ordinal))
            return false;

        var nextReal = realPrompts.NextSequence(owner.Sequence);
        return IsEndTurnSiblingPhantom(candidate, owner.Sequence, nextReal, tools);
    }

    /// <summary>
    /// The real prompt that owns a skipped sibling phantom. Older prompts sit behind
    /// it, matching the real-prompt path. The first skipped phantom wins.
    /// </summary>
    private static void RememberPhantomBarrier(
        IReadOnlyList<TranscriptPromptSpan.PromptRow> raw,
        long ownerSequence,
        long boundarySequence,
        ref TranscriptPromptSpan.PromptRow? barrier,
        ref long barrierBoundary)
    {
        if (barrier is not null)
            return;
        foreach (var row in raw)
        {
            if (row.Sequence != ownerSequence)
                continue;
            barrier = row;
            barrierBoundary = boundarySequence;
            return;
        }
    }

    private static bool IsEndTurnSiblingPhantom(
        BoundaryRow end,
        long ownerSequence,
        long? nextRealPrompt,
        IReadOnlyList<ToolSiblingRow> tools)
    {
        if (!string.Equals(end.StopReason, TranscriptKinds.StopReasons.EndTurn, StringComparison.Ordinal))
            return false;
        if (string.IsNullOrEmpty(end.ApiCallId))
            return false;

        foreach (var tool in tools)
        {
            if (!string.Equals(tool.ApiCallId, end.ApiCallId, StringComparison.Ordinal))
                continue;
            if (tool.Sequence <= ownerSequence)
                continue;
            if (nextRealPrompt is long cap && tool.Sequence >= cap)
                continue;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Tool rows that can prove an <c>end_turn</c> sibling. Empty when no boundary can
    /// match. The floor is the first time-eligible prompt, so the read stays on
    /// <c>(AgentSessionId, Kind, Sequence)</c>.
    /// </summary>
    private static async Task<List<ToolSiblingRow>> LoadSiblingToolsAsync(
        AppDbContext db,
        Guid sessionId,
        IReadOnlyList<TranscriptPromptSpan.PromptRow> raw,
        IReadOnlyList<BoundaryRow> boundaries,
        DateTime? dispatchedAt,
        CancellationToken ct)
    {
        long? floor = null;
        foreach (var row in raw)
        {
            if (!IsTimeEligible(row, dispatchedAt))
                continue;
            floor = row.Sequence;
            break;
        }

        if (floor is not long start)
            return [];

        var needed = false;
        foreach (var boundary in boundaries)
        {
            if (string.Equals(boundary.StopReason, TranscriptKinds.StopReasons.EndTurn, StringComparison.Ordinal)
                && !string.IsNullOrEmpty(boundary.ApiCallId))
            {
                needed = true;
                break;
            }
        }

        if (!needed)
            return [];

        return await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.ToolCall
                && t.ApiCallId != null
                && t.Sequence > start)
            .Select(t => new ToolSiblingRow(t.Sequence, t.ApiCallId!))
            .ToListAsync(ct);
    }

    /// <summary>
    /// The last row whose sequence is strictly below <paramref name="sequence"/>.
    /// <paramref name="rows"/> is ordered by sequence. Each call reads that list;
    /// nothing is cached, so the answer is not stale.
    /// </summary>
    internal static TranscriptPromptSpan.PromptRow? LastBefore(
        IReadOnlyList<TranscriptPromptSpan.PromptRow> rows, long sequence)
    {
        var lo = 0;
        var hi = rows.Count - 1;
        var found = -1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (rows[mid].Sequence < sequence)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return found < 0 ? null : rows[found];
    }

    internal static RealPromptIndex CreateRealPromptIndex(
        IReadOnlyList<TranscriptPromptSpan.PromptRow> rows,
        AgentKind sessionKind,
        IReadOnlySet<Guid> rulesIds,
        IReadOnlySet<string> invoked,
        Action? onClassification = null) => new(rows, row =>
        {
            onClassification?.Invoke();
            return IsTurnOwningKind(row.Kind) && !IsHousekeepingRow(sessionKind, row, rulesIds, invoked);
        });

    /// <summary>
    /// Per-selection ordered index of real prompts. Classify each source row once;
    /// subsequent strict-bound queries use binary search, preserving source order
    /// among equal sequences (NewestBetween returns the last such row).
    /// </summary>
    internal sealed class RealPromptIndex(
        IReadOnlyList<TranscriptPromptSpan.PromptRow> rows,
        Func<TranscriptPromptSpan.PromptRow, bool> isReal)
    {
        private readonly TranscriptPromptSpan.PromptRow[] _rows = rows.Where(isReal).ToArray();

        internal TranscriptPromptSpan.PromptRow? NewestBetween(long? after, long before)
        {
            var index = InsertionPoint(before, includeEqual: false) - 1;
            if (index < 0 || (after is long floor && _rows[index].Sequence <= floor))
                return null;
            return _rows[index];
        }

        internal long? NextSequence(long after)
        {
            var index = InsertionPoint(after, includeEqual: true);
            return index < _rows.Length ? _rows[index].Sequence : null;
        }

        // The first row >= sequence, or > sequence when equal rows are included
        // in the prefix. Keeping these bounds explicit also handles duplicate keys.
        private int InsertionPoint(long sequence, bool includeEqual)
        {
            var low = 0;
            var high = _rows.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                var value = _rows[middle].Sequence;
                if (value < sequence || (includeEqual && value == sequence))
                    low = middle + 1;
                else
                    high = middle;
            }
            return low;
        }
    }

    internal readonly record struct BoundaryRow(long Sequence, string? Kind, string? StopReason, string? ApiCallId);

    internal readonly record struct ToolSiblingRow(long Sequence, string ApiCallId);
}
