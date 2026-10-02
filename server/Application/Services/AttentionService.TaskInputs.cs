using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed partial class AttentionService
{
    private async Task<List<AttentionItemDto>> BuildTaskInputUnreadableItemsAsync(
        IReadOnlyList<AgentTask> tasks, CancellationToken ct)
    {
        var bound = tasks.Where(t => t.AgentSessionId is not null).ToList();
        if (bound.Count == 0)
            return [];
        var sessionIds = bound.Select(t => t.AgentSessionId!.Value).Distinct().ToArray();
        var taskIds = bound.Select(t => t.Id).ToArray();
        var inputEvents = await _db.AgentTaskEvents.AsNoTracking()
            .Where(e => taskIds.Contains(e.AgentTaskId) && e.InputBody != null
                && (e.Type == AgentTaskEventType.Refined || e.Type == AgentTaskEventType.Replied))
            .Select(e => new { e.Id, e.AgentTaskId, e.AgentSessionId, e.Type })
            .ToDictionaryAsync(e => e.Id, ct);
        var inputKeys = inputEvents.Values
            .Select(e => AgentTaskInputService.ConversationKey(e.AgentTaskId, e.Id)).ToArray();
        var rows = await _db.SessionQueuedMessages.AsNoTracking()
            .Where(m => sessionIds.Contains(m.AgentSessionId)
                && m.Origin == QueuedMessageOrigin.Delegation
                && ((m.ConversationKey != null && inputKeys.Contains(m.ConversationKey))
                    || (m.ConversationKey == null && m.Body.Contains("REFINEMENT")
                        && m.Body.Contains(".md"))))
            .OrderBy(m => m.Sequence)
            .Select(m => new TaskInputQueueRow(m.Id, m.AgentSessionId, m.Sequence, m.Body,
                m.ConversationKey, m.RemoteSpillRelativePath)).ToListAsync(ct);
        var inputs = new Dictionary<Guid, List<TaskInputUnreadableInput>>();
        foreach (var task in bound)
        {
            var sessionId = task.AgentSessionId!.Value;
            var marker = DelegationReportFormatter.TaskMarker(task.Id);
            var taskRows = rows.Where(r => r.AgentSessionId == sessionId).Select(r =>
            {
                var owned = AgentTaskInputService.TryParseConversationKey(
                    r.ConversationKey, out var keyedTaskId, out var eventId)
                    && keyedTaskId == task.Id
                    && inputEvents.TryGetValue(eventId, out var e)
                    && e.AgentTaskId == task.Id && e.AgentSessionId == sessionId;
                var legacy = !owned && r.ConversationKey is null
                    && r.Body.Contains(marker, StringComparison.Ordinal)
                    && r.Body.Contains("REFINEMENT", StringComparison.Ordinal)
                    && TaskInputReadFailure.LegacyLocation(r.Body) is not null;
                var location = owned
                    ? r.RemoteSpillRelativePath ?? AgentTaskInputService.Route(task.Id, eventId)
                    : legacy ? TaskInputReadFailure.LegacyLocation(r.Body) : null;
                return new TaskInputUnreadableInput(r, location,
                    owned && inputEvents[eventId].Type == AgentTaskEventType.Replied);
            }).Where(x => x.Location is not null).ToList();
            if (taskRows.Count > 0)
                inputs[task.Id] = taskRows;
        }
        if (inputs.Count == 0)
            return [];

        // The queue stores only the latest retry baseline, not the first attempt. Find the
        // earliest complete delivered input with the existing matcher. LIKE is a necessary
        // subsequence filter, not a receipt: ANSI and whitespace normalization remove raw
        // characters, so a literal Contains(body) would silently lose complete prompts.
        var transcripts = new Dictionary<Guid, List<TaskInputTranscript>>();
        foreach (var group in inputs.Values.SelectMany(x => x).GroupBy(x => x.Row.AgentSessionId))
        {
            var sessionId = group.Key;
            var sessionInputs = group.ToList();
            var patterns = sessionInputs.Select(x => TaskInputPromptPattern(x.Row.Body)).Distinct().ToArray();
            long? firstPromptSequence = null;
            var candidates = _db.TranscriptEntries.AsNoTracking()
                .Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.UserPrompt
                    && t.Text != null && patterns.Any(pattern => EF.Functions.Like(t.Text, pattern, "!")))
                .OrderBy(t => t.Sequence)
                .Select(t => new TaskInputTranscript(t.Sequence, t.Kind, t.Text, t.Timestamp ?? t.CreatedAt));
            await foreach (var prompt in candidates.AsAsyncEnumerable().WithCancellation(ct))
            {
                if (!sessionInputs.Any(input => PromptSubmissionMatch.IsCompleteIn(input.Row.Body, prompt.Text)))
                    continue;
                firstPromptSequence = prompt.Sequence;
                break;
            }
            if (firstPromptSequence is not long floor)
                continue;
            transcripts[sessionId] = await _db.TranscriptEntries.AsNoTracking()
                .Where(t => t.AgentSessionId == sessionId && t.Sequence >= floor
                    && (t.Kind == TranscriptKinds.UserPrompt || t.Kind == TranscriptKinds.AssistantText))
                .OrderBy(t => t.Sequence)
                .Select(t => new TaskInputTranscript(t.Sequence, t.Kind, t.Text, t.Timestamp ?? t.CreatedAt))
                .ToListAsync(ct);
        }
        if (transcripts.Count == 0)
            return [];

        // Any Delegation body can supersede an episode, including a canceled ordinary brief.
        // Retain that rule while fetching only bodies for sessions with complete input receipts.
        var relevantSessionIds = transcripts.Keys.ToArray();
        var delegationBodies = await _db.SessionQueuedMessages.AsNoTracking()
            .Where(m => relevantSessionIds.Contains(m.AgentSessionId)
                && m.Origin == QueuedMessageOrigin.Delegation)
            .Select(m => new { m.AgentSessionId, m.Body }).ToListAsync(ct);
        var items = new List<AttentionItemDto>();
        foreach (var task in bound)
        {
            var sessionId = task.AgentSessionId!.Value;
            if (!inputs.TryGetValue(task.Id, out var taskRows)
                || !transcripts.TryGetValue(sessionId, out var sessionEntries))
                continue;
            var prompts = sessionEntries.Where(t => t.Kind == TranscriptKinds.UserPrompt
                && t.Text is not null).ToList();

            foreach (var input in taskRows)
            {
                var prompt = prompts.FirstOrDefault(p =>
                    PromptSubmissionMatch.IsCompleteIn(input.Row.Body, p.Text));
                if (prompt is null)
                    continue;
                var nextPrompt = prompts.FirstOrDefault(p => p.Sequence > prompt.Sequence);
                var end = nextPrompt?.Sequence ?? long.MaxValue;
                var soleInput = taskRows.Count(x => x.Row.Sequence <= input.Row.Sequence) == 1;
                var complaint = sessionEntries.FirstOrDefault(t => t.Kind == TranscriptKinds.AssistantText
                    && t.Sequence > prompt.Sequence && t.Sequence < end
                    && TaskInputReadFailure.Matches(t.Text, input.Location!, input.IsReply, soleInput));
                if (complaint is null)
                    continue;

                // A later complete caller input begins a new turn and supersedes this episode.
                if (prompts.Any(p => p.Sequence > complaint.Sequence && delegationBodies.Any(r =>
                        r.AgentSessionId == sessionId
                        && PromptSubmissionMatch.IsCompleteIn(r.Body, p.Text))))
                    continue;

                items.Add(new AttentionItemDto(
                    AttentionKind.TaskInputUnreadable, AlertSeverity.Warning,
                    task.Id, sessionId, task.AgentId, input.Row.Id,
                    $"Input unreadable: {DelegationReportFormatter.Short(task.Id)}",
                    "Delegate says it cannot read a caller input",
                    $"Assistant transcript #{complaint.Sequence}: {(complaint.Text ?? string.Empty)[..Math.Min(240, complaint.Text?.Length ?? 0)]}",
                    complaint.At, null,
                    [AttentionAction.OpenDrawer], task.CardId,
                    ConditionKey: $"task-input-unreadable:{task.Id:D}:{input.Row.Id:D}"));
            }
        }
        return items;
    }

    private sealed record TaskInputQueueRow(Guid Id, Guid AgentSessionId, long Sequence,
        string Body, string? ConversationKey, string? RemoteSpillRelativePath);
    private sealed record TaskInputUnreadableInput(TaskInputQueueRow Row, string? Location, bool IsReply);
    private sealed record TaskInputTranscript(long Sequence, string Kind, string? Text, DateTime At);

    private static string TaskInputPromptPattern(string body)
    {
        if (!PromptSubmissionMatch.RequiresTextMatch(body))
            return "%"; // The existing matcher accepts any non-null prompt for short bodies.
        var characters = PromptSubmissionMatch.Normalize(body).Replace(" ", "", StringComparison.Ordinal)
            .EnumerateRunes().Take(32).Select(rune => rune.ToString()
                .Replace("!", "!!", StringComparison.Ordinal)
                .Replace("%", "!%", StringComparison.Ordinal)
                .Replace("_", "!_", StringComparison.Ordinal));
        return "%" + string.Join("%", characters) + "%";
    }
}
