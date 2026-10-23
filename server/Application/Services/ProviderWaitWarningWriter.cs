using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>One transaction owns the episode transition and its immutable caller obligation.</summary>
public sealed class ProviderWaitWarningWriter(IServiceScopeFactory scopes, TaskProviderWaitReader reader,
    IEventBus events, TimeProvider clock, IOptions<DelegationSettings> settings, LandDeliveryBoundary? boundary = null)
{
    private sealed record Episode(int Version, long Revision, Guid Id, string State,
        TaskProviderWaitBinding Binding, Guid Epoch, long? Prompt, string Call, string Dialog,
        DateTime ObservedAt, long OutputSequence, long InputRevision);
    private const string Prefix = "ProviderInputWait ";
    private static Episode? Parse(string? detail)
    {
        if (detail is null || !detail.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        try { return JsonSerializer.Deserialize<Episode>(detail[Prefix.Length..]) is { Version: 1, Binding: not null } value ? value : null; }
        catch (JsonException) { return null; }
    }

    internal async Task<bool> RecordAsync(Guid taskId, TaskProviderWaitFacts? facts, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var task = await db.AgentTasks.FromSqlInterpolated($"SELECT * FROM \"AgentTasks\" WHERE \"Id\" = {taskId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (task is null || task.Status is not (AgentTaskStatus.Dispatched or AgentTaskStatus.Working)
            || task.AgentSessionId is not Guid sessionId) return false;
        var session = await db.AgentSessions.FromSqlInterpolated($"SELECT * FROM \"AgentSessions\" WHERE \"Id\" = {sessionId} FOR SHARE SKIP LOCKED")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (session is null) return false;
        if (facts is not null && (!facts.Binding.Matches(task, session)
            || !facts.Fresh(clock.GetUtcNow().UtcDateTime) || await reader.CachedAsync(taskId, ct) != facts)) return false;
        var details = await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == taskId
            && e.Type == AgentTaskEventType.Warning && e.Detail.StartsWith(Prefix)).Select(e => e.Detail).ToListAsync(ct);
        var episodes = details.Select(Parse).OfType<Episode>().ToList();
        var revision = episodes.Select(e => e.Revision).DefaultIfEmpty().Max();
        var prior = episodes.OrderByDescending(e => e.Revision).FirstOrDefault();
        var prompt = await db.TranscriptEntries.Where(e => e.AgentSessionId == sessionId && e.Kind == TranscriptKinds.UserPrompt)
            .MaxAsync(e => (long?)e.Sequence, ct);
        var call = await db.TranscriptEntries.AsNoTracking().Where(e => e.AgentSessionId == sessionId
            && e.Kind == TranscriptKinds.ToolCall && (!prompt.HasValue || e.Sequence > prompt.Value))
            .OrderByDescending(e => e.Sequence).FirstOrDefaultAsync(ct);
        if (call is not null && await db.TranscriptEntries.AnyAsync(e => e.AgentSessionId == sessionId
            && e.Kind == TranscriptKinds.ToolResult && e.Sequence > call.Sequence
            && (call.ToolUseId == null || e.ToolUseId == call.ToolUseId), ct)) call = null;
        var callKey = call is null ? "unknown" : string.IsNullOrEmpty(call.ToolUseId)
            ? "sequence:" + call.Sequence : "tool:" + DelegationNoteDigest.Compute(call.ToolUseId);
        var now = clock.GetUtcNow().UtcDateTime;
        void Add(Episode episode) => db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(), AgentTaskId = taskId, Type = AgentTaskEventType.Warning,
            At = now, Detail = Prefix + JsonSerializer.Serialize(episode),
        });
        if (prior is { State: "open" })
        {
            var same = prior.Binding.Matches(task, session) && prior.Prompt == prompt && prior.Call == callKey
                && (facts is null || (prior.Epoch == facts.Observation.RuntimeEpoch && prior.Dialog == facts.DialogLabel));
            if (same && facts?.Observation.State != ProviderDialogState.Clear) return false;
            Add(prior with { State = "resolved", Revision = ++revision });
        }
        if (facts is { IsWait: true })
        {
            var episode = new Episode(1, ++revision, Guid.NewGuid(), "open", facts.Binding,
                facts.Observation.RuntimeEpoch, prompt, callKey, facts.DialogLabel, facts.CompletedAt,
                facts.Observation.OutputSequence, facts.Observation.InputRevision);
            var warningId = Guid.NewGuid();
            db.AgentTaskEvents.Add(new AgentTaskEvent { Id = warningId, AgentTaskId = taskId,
                Type = AgentTaskEventType.Warning, At = now, Detail = Prefix + JsonSerializer.Serialize(episode) });
            var shortId = DelegationReportFormatter.Short(taskId);
            var cardLabel = task.CardId is Guid cardId
                ? await db.Cards.Where(c => c.Id == cardId).Select(c => c.Identifier).FirstOrDefaultAsync(ct) : null;
            cardLabel ??= "task " + shortId;
            var pending = await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == sessionId && m.Status == QueuedMessageStatus.Pending, ct);
            var receipt = pending == 0 ? "none queued" : "not confirmed";
            var last = await TaskDeadlinePolicy.LoadLastEntryAsync(db, sessionId, ct);
            var local = last?.Kind == TranscriptKinds.ToolCall;
            var phase = local ? settings.Value.LocalExecutionDeadlineMinutes : settings.Value.ModelWaitDeadlineMinutes;
            var ceiling = TaskDeadlinePolicy.CeilingMinutes(settings.Value, task.Role);
            var limit = ceiling > 0 && (phase <= 0 || ceiling < phase) ? ceiling : phase;
            var clockLabel = limit == ceiling && ceiling > 0 && (phase <= 0 || ceiling < phase) ? "role-ceiling" : local ? "local-tool" : "model-wait";
            var body = $"[antiphon-permission-wait:{shortId}] Decision needed for {cardLabel}: Claude {facts.DialogLabel} observed at {facts.CompletedAt:O} in session {sessionId}.\n"
                + $"The task remains {task.Status}. " + (call is null ? "Tool execution attribution is unknown." : "The outstanding ToolCall does not prove that execution started.")
                + $" Pending refinements: {pending}; receipt: {receipt}.\n"
                + "Inspect the current session and choose an authorized answer or cancellation. Antiphon has sent no approval or cancellation key. Refine may remain queued while the dialog is open; only a complete UserPrompt confirms delivery.\n"
                + $"The existing {limit}-minute {clockLabel} deadline remains in force. Check current task and session state before acting on this note.";
            db.AgentTaskLandNotifications.Add(new AgentTaskLandNotification
            {
                Id = Guid.NewGuid(), TaskId = taskId, SourceEventId = warningId,
                Kind = LandNotificationKind.ProviderInputWait, ReplyTo = task.ReplyTo, ParentSessionId = task.ParentSessionId,
                Body = body, ContentDigest = DispatchBaseNotificationPayload.Digest(task.ReplyTo, task.ParentSessionId, body),
                CreatedAt = now, NextAttemptAt = now,
                State = task.ReplyTo == AgentTaskReplyTo.None ? LandNotificationState.NotRequired
                    : task.ReplyTo != AgentTaskReplyTo.Session || task.ParentSessionId is null || task.ParentSessionId == Guid.Empty
                        ? LandNotificationState.DestinationUnavailable : LandNotificationState.Queued,
            });
        }
        if (!db.ChangeTracker.HasChanges()) return false;
        if (boundary is not null) await boundary.ReachedAsync("provider-wait-before-save", taskId, taskId, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await events.PublishToAllAsync("AgentTaskChanged", new { taskId, rootId = task.RootTaskId }, ct);
        return true;
    }
}
