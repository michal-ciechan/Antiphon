using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class AgentTaskLandReceiptTests
{
    [Test]
    [Arguments("catch-up")]
    [Arguments("receipt-save")]
    [Arguments("missing-verdict")]
    [Arguments("spilled")]
    public async Task C467_V12_CatchUpAndRecoverReceiptWithoutRetyping(string cut)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await SeedAsync(db, h.SessionId);
        var service = new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId);
        if (cut == "spilled")
        {
            row.Body = await h.Queue.SpillQueueBodyAsync(h.SessionId, note.Body, "land-" + note.Id.ToString("N"), null, db, CancellationToken.None,
                new DelegationSettings().CeilingsFor(PtyBackend.InboxConhost, "owned spill control"));
            row.Body.Length.ShouldBeLessThan(note.Body.Length);
            var files = Directory.GetFiles(h.TempRoot, "land-" + note.Id.ToString("N") + ".md", SearchOption.AllDirectories);
            files.Length.ShouldBe(1); (await File.ReadAllTextAsync(files[0])).ShouldBe(note.Body);
        }
        row.Status = QueuedMessageStatus.Sent; row.DeliveryAttempts = 1; row.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1);
        row.LastDeliveryBaselineSequence = 10; row.DeliveryVerdict = null;
        db.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 10,
            Kind = TranscriptKinds.AssistantText, Text = "baseline", CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow.AddMinutes(-1) });
        await db.SaveChangesAsync();
        h.Runner.SetTranscript(new(h.SessionId, [new SessionRunnerTranscriptEvent(h.SessionId, 11, TranscriptKinds.UserPrompt,
            "c467-native-" + note.Id.ToString("N"), null, DateTimeOffset.UtcNow, "user", row.Body.Replace("\n", ""), null, null, null, null, null)], 11));
        (await db.TranscriptEntries.CountAsync(p => p.AgentSessionId == h.SessionId && p.Kind == TranscriptKinds.UserPrompt)).ShouldBe(0);
        if (cut == "receipt-save")
        {
            await new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System, new ReceiptSaveFailure()).ReconcileAsync(note.Id, CancellationToken.None);
            await db.Entry(note).ReloadAsync(); note.ConfirmedAt.ShouldBeNull();
        }
        await using var restarted = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var recovery = new AgentTaskLandNotificationService(restarted, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
        await recovery.ReconcileAsync(note.Id, CancellationToken.None); await recovery.ReconcileAsync(note.Id, CancellationToken.None);
        var saved = await restarted.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id);
        saved.State.ShouldBe(LandNotificationState.Confirmed); saved.ConfirmingPromptSequence.ShouldBeGreaterThan(10);
        (await restarted.TranscriptEntries.CountAsync(p => p.AgentSessionId == h.SessionId && p.Kind == TranscriptKinds.UserPrompt)).ShouldBe(1);
        h.Adapter.Inputs.ShouldBeEmpty("native catch-up and receipt persistence must not retype");
    }

    private sealed class ReceiptSaveFailure : LandDeliveryBoundary
    {
        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct) =>
            boundary == "receipt-before-save" ? Task.FromException(new IOException("owned receipt save failure")) : Task.CompletedTask;
    }

    [Test]
    [Arguments("attempted")]
    [Arguments("parked")]
    [Arguments("truncated")]
    [Arguments("canceled")]
    [Arguments("confirmed")]
    public async Task C467_V13_RetentionCancellationAndSupersession(string state)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await SeedAsync(db, h.SessionId);
        var notifier = new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
        await notifier.ReconcileAsync(note.Id, CancellationToken.None);
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId);
        row.CreatedAt = DateTime.UtcNow.AddDays(-1000); row.DeliveryAttempts = state is "parked" or "truncated" ? 20 : 1;
        row.LastDeliveryBaselineSequence = 10; row.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1);
        row.Status = state is "parked" or "truncated" or "canceled" ? QueuedMessageStatus.Pending : QueuedMessageStatus.Sent;
        if (state == "truncated") row.DeliveryVerdict = DeliveryVerdict.Truncated;
        var control = Guid.NewGuid();
        db.SessionQueuedMessages.Add(new SessionQueuedMessage { Id = control, AgentSessionId = h.SessionId, Body = "ordinary history",
            CreatedAt = row.CreatedAt, Status = QueuedMessageStatus.Sent });
        if (state == "confirmed") db.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = h.SessionId,
            Sequence = 11, Kind = TranscriptKinds.UserPrompt, Text = row.Body, Timestamp = DateTime.UtcNow, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        if (state == "canceled") await h.Queue.CancelAsync(h.SessionId, row.Id, CancellationToken.None);
        await notifier.ReconcileAsync(note.Id, CancellationToken.None); await db.Entry(note).ReloadAsync();
        note.ConfirmedAt.HasValue.ShouldBe(state == "confirmed");
        if (state == "canceled") note.State.ShouldBe(LandNotificationState.Canceled);
        var audit = Options.Create(new AuditSettings());
        var retention = new DataRetentionService(db, Options.Create(new RetentionSettings()), audit, TimeProvider.System,
            NullLogger<DataRetentionService>.Instance, new AuditService(db, audit));
        await retention.PruneQueuedMessagesAsync(CancellationToken.None);
        (await db.SessionQueuedMessages.AnyAsync(m => m.Id == control)).ShouldBeFalse();
        (await db.SessionQueuedMessages.AnyAsync(m => m.Id == row.Id)).ShouldBe(state != "confirmed");
        await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteDeleteAsync();
        (await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id)).ConfirmedAt.HasValue.ShouldBe(state == "confirmed");
        h.Adapter.Inputs.ShouldBeEmpty();
    }
    [Test]
    [Arguments("complete")]
    [Arguments("flattened")]
    [Arguments("queue-enqueue")]
    [Arguments("queued-prompt")]
    [Arguments("sent")]
    [Arguments("screen-delivered")]
    [Arguments("late-confirmed")]
    [Arguments("wrong-session")]
    [Arguments("wrong-identity")]
    [Arguments("old-sequence")]
    [Arguments("old-time")]
    [Arguments("head-only")]
    [Arguments("head-tail-splice")]
    [Arguments("unrelated")]
    public async Task C467_V11_RejectFalseReceipts(string evidence)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await SeedAsync(db, h.SessionId);
        var service = new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        await db.Entry(note).ReloadAsync();
        note.QueueMessageId.ShouldNotBeNull();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId);
        row.DeliveryAttempts = 1;
        row.Status = QueuedMessageStatus.Sent;
        row.LastDeliveryBaselineSequence = evidence == "old-time" ? null : 10;
        row.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1);
        row.DeliveryVerdict = evidence == "screen-delivered" ? DeliveryVerdict.Delivered
            : evidence == "late-confirmed" ? DeliveryVerdict.LateConfirmed : null;
        if (evidence is not ("sent" or "screen-delivered" or "late-confirmed"))
        {
            var session = h.SessionId;
            if (evidence == "wrong-session")
            {
                session = Guid.NewGuid();
                db.AgentSessions.Add(new AgentSession { Id = session, CreatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow });
            }
            var text = evidence switch
            {
                "flattened" => row.Body.Replace("\n", ""),
                "wrong-identity" => row.Body.Replace(note.Id.ToString("N"), Guid.NewGuid().ToString("N")),
                "head-only" => row.Body[..200],
                "head-tail-splice" => row.Body[..200] + row.Body[^100..],
                "unrelated" => "unrelated later caller prompt",
                _ => row.Body,
            };
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = session,
                Sequence = evidence == "old-sequence" ? 10 : 11,
                Kind = evidence == "queue-enqueue" ? TranscriptKinds.QueueEnqueue : evidence == "queued-prompt" ? TranscriptKinds.QueuedUserPrompt : TranscriptKinds.UserPrompt,
                Text = text, Timestamp = evidence == "old-time" ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        await db.Entry(note).ReloadAsync();
        var confirmed = evidence is "complete" or "flattened";
        note.ConfirmedAt.HasValue.ShouldBe(confirmed);
        note.ConfirmingPromptSequence.ShouldBe(confirmed ? 11L : null);
        h.Adapter.Inputs.ShouldBeEmpty("receipt reconciliation must never type");
    }

    internal static async Task<AgentTaskLandNotification> SeedAsync(AppDbContext db, Guid session, AgentTaskReplyTo replyTo = AgentTaskReplyTo.Session, string? detail = null)
    {
        var taskId = Guid.NewGuid();
        var task = new AgentTask { Id = taskId, RootTaskId = taskId, Title = "C467 receipt", Goal = "receipt fixture",
            WorkingDirectory = Path.GetTempPath(), Status = AgentTaskStatus.Succeeded, ReplyTo = replyTo,
            ParentSessionId = session, CreatedAt = DateTime.UtcNow };
        var request = new AgentTaskLandRequest { Id = Guid.NewGuid(), TaskId = taskId, ReplyTo = replyTo,
            ParentSessionId = session, RequestedAt = DateTime.UtcNow, LastEvaluatedAt = DateTime.UtcNow, LastProgressAt = DateTime.UtcNow,
            State = LandRequestState.Completed, IsPending = false };
        var terminal = new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = taskId, LandRequestId = request.Id, IsLandTerminal = true,
            Type = AgentTaskEventType.LandRefused, At = DateTime.UtcNow,
            Detail = detail ?? "distinct beginning\n" + string.Join("\n", Enumerable.Range(0, 50).Select(i => $"line {i}: preserved outcome evidence and immutable payload")) + "\ndistinct final tail" };
        request.TerminalEventId = terminal.Id;
        task.CurrentLandRequestId = request.Id;
        var note = LandNotificationPayload.Create(request, terminal, LandNotificationKind.Outcome);
        db.AgentTasks.Add(task);
        db.AgentTaskLandRequests.Add(request);
        db.AgentTaskEvents.Add(terminal);
        db.AgentTaskLandNotifications.Add(note);
        await db.SaveChangesAsync();
        return note;
    }
}
