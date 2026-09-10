using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class AgentTaskLandReceiptTests
{
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
                db.AgentSessions.Add(new AgentSession { Id = session, WorkingDirectory = h.TempRoot, CreatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow });
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

    internal static async Task<AgentTaskLandNotification> SeedAsync(AppDbContext db, Guid session)
    {
        var taskId = Guid.NewGuid();
        var task = new AgentTask { Id = taskId, RootTaskId = taskId, Title = "C467 receipt", Goal = "receipt fixture",
            WorkingDirectory = Path.GetTempPath(), Status = AgentTaskStatus.Succeeded, ReplyTo = AgentTaskReplyTo.Session,
            ParentSessionId = session, CreatedAt = DateTime.UtcNow };
        var request = new AgentTaskLandRequest { Id = Guid.NewGuid(), TaskId = taskId, ReplyTo = AgentTaskReplyTo.Session,
            ParentSessionId = session, RequestedAt = DateTime.UtcNow, LastEvaluatedAt = DateTime.UtcNow, LastProgressAt = DateTime.UtcNow,
            State = LandRequestState.Completed, IsPending = false };
        var terminal = new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = taskId, LandRequestId = request.Id, IsLandTerminal = true,
            Type = AgentTaskEventType.LandRefused, At = DateTime.UtcNow,
            Detail = "distinct beginning\n" + string.Join("\n", Enumerable.Range(0, 50).Select(i => $"line {i}: preserved outcome evidence and immutable payload")) + "\ndistinct final tail" };
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
