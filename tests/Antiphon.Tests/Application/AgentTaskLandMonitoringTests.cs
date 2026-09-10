using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Antiphon.Server.Application.Dtos;
using Antiphon.SessionRunner.Contracts;
using System.Text.Json;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class AgentTaskLandMonitoringTests
{
    [Test]
    [Arguments(0, 900, false)]
    [Arguments(300, 300, false)]
    [Arguments(900, 300, false)]
    [Arguments(300, 900, true)]
    public void C467_V15_ThresholdConfiguration(int warning, int error, bool valid)
    {
        var settings = new DelegationSettings { LandWarningSeconds = warning, LandErrorSeconds = error };
        new DelegationSettingsValidator().Validate(null, settings).Succeeded.ShouldBe(valid);
        new DelegationSettings().LandWarningSeconds.ShouldBe(300); new DelegationSettings().LandErrorSeconds.ShouldBe(900);
    }
    [Test]
    [Arguments(LandRequestState.Held)]
    [Arguments(LandRequestState.Queued)]
    [Arguments(LandRequestState.Running)]
    [Arguments(LandRequestState.NeedsResolution)]
    public async Task C467_V16_AttentionSurvivesRecencyAndDeduplicates(LandRequestState state)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await AgentTaskLandReceiptTests.SeedAsync(db, h.SessionId);
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == note.RequestId);
        var holder = Guid.NewGuid(); var old = DateTime.UtcNow.AddDays(-10);
        request.IsPending = true; request.State = state; request.HoldingTaskId = holder; request.HoldingTaskStatus = AgentTaskStatus.Blocked;
        request.HoldReasonCode = "repository_or_source_writer"; request.HeldSince = old; request.LastProgressAt = old; request.RequestedAt = old;
        note.CreatedAt = old;
        await db.SaveChangesAsync();
        var notifier = new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
        await notifier.ReconcileAsync(note.Id, CancellationToken.None);
        var unrelated = Guid.NewGuid();
        db.SessionQueuedMessages.Add(new SessionQueuedMessage { Id = unrelated, AgentSessionId = h.SessionId,
            Body = "unrelated parked input", DeliveryAttempts = 20, Status = QueuedMessageStatus.Pending, CreatedAt = old });
        await db.SaveChangesAsync();
        async Task<AttentionDto> ReadAsync()
        {
            await using var fresh = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            return await new AttentionService(fresh, h.Runner, Options.Create(new SupervisionSettings()), Options.Create(new DelegationSettings()),
                TimeProvider.System, NullLogger<AttentionService>.Instance).GetAsync(CancellationToken.None);
        }
        var result = await ReadAsync();
        var held = result.Items.Single(i => i.LandRequestId == request.Id && i.LandNotificationId == null);
        held.Kind.ShouldBe(state == LandRequestState.Held ? AttentionKind.LandHeld : AttentionKind.LandNoProgress);
        held.ConditionKey.ShouldBe($"land:{request.Id:N}:{(state == LandRequestState.Held ? "held" : "progress")}");
        held.HoldingTaskId.ShouldBe(holder); held.Severity.ShouldBe(AlertSeverity.Error);
        held.Headline.ShouldContain("no progress for");
        (await ReadAsync()).Items.Single(i => i.LandRequestId == request.Id && i.LandNotificationId == null).ConditionKey.ShouldBe(held.ConditionKey);
        var outcome = result.Items.Single(i => i.LandNotificationId == note.Id);
        outcome.ConditionKey.ShouldBe($"land:{note.Id:N}:receipt"); outcome.SessionId.ShouldBe(h.SessionId);
        outcome.Actions.ShouldBe([AttentionAction.OpenDrawer]); outcome.MessageId.ShouldBe(note.QueueMessageId);
        result.Items.ShouldNotContain(i => i.MessageId == note.QueueMessageId && (i.Kind == AttentionKind.CallerNoteUndelivered || i.Kind == AttentionKind.ParkedMessage));
        result.Items.ShouldContain(i => i.MessageId == unrelated && i.Kind == AttentionKind.ParkedMessage);
        (await ReadAsync()).Items.Single(i => i.LandNotificationId == note.Id).ConditionKey.ShouldBe(outcome.ConditionKey);
        JsonSerializer.Serialize(outcome, new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldContain("landNotificationId");
        ((int)AttentionKind.LandHeld).ShouldBe(35); ((int)AttentionKind.LandNoProgress).ShouldBe(36); ((int)AttentionKind.LandOutcomeUnconfirmed).ShouldBe(37);
        ((int)AttentionKind.BlockedQuestion).ShouldBe(0); ((int)AttentionKind.ParkedMessage).ShouldBe(1);
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId);
        row.DeliveryAttempts = 1; row.Status = QueuedMessageStatus.Sent; row.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1); row.LastDeliveryBaselineSequence = 10;
        db.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 11,
            Kind = TranscriptKinds.UserPrompt, Text = row.Body, Timestamp = DateTime.UtcNow, CreatedAt = DateTime.UtcNow });
        request.IsPending = false; request.State = LandRequestState.Completed; request.HoldingTaskId = null; request.HoldReasonCode = null;
        await db.SaveChangesAsync(); await notifier.ReconcileAsync(note.Id, CancellationToken.None);
        (await ReadAsync()).Items.ShouldNotContain(i => i.LandNotificationId == note.Id || i.Kind == AttentionKind.LandHeld && i.TaskId == note.TaskId);
        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == note.TaskId)).Status.ShouldBe(AgentTaskStatus.Succeeded);
        h.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    [Arguments(false, LandPublicationOutcome.Landed, LandCleanupStatus.Complete)]
    [Arguments(false, LandPublicationOutcome.AlreadyPresent, LandCleanupStatus.Pending)]
    [Arguments(true, LandPublicationOutcome.Landed, LandCleanupStatus.Refused)]
    [Arguments(true, LandPublicationOutcome.AlreadyPresent, LandCleanupStatus.Pending)]
    public async Task C467_V15_AgedPayloadPreservesLandingEvidence(bool heldCleanup, LandPublicationOutcome publication, LandCleanupStatus cleanup)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var now = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);
        var clock = new FakeTimeProvider(now);
        var taskId = Guid.NewGuid();
        var operation = new AgentTaskLanding { Id = Guid.NewGuid(), TaskId = taskId, Publication = publication,
            Cleanup = cleanup, Mode = LandOperationMode.CleanupRetry, CreatedAt = now, UpdatedAt = now };
        var request = new AgentTaskLandRequest { Id = Guid.NewGuid(), TaskId = taskId, ReplyTo = AgentTaskReplyTo.Session,
            State = heldCleanup ? LandRequestState.Held : LandRequestState.Completed, IsPending = heldCleanup,
            RequestedAt = now, LastProgressAt = now, LastEvaluatedAt = now,
            HoldReasonCode = heldCleanup ? "repository_or_source_writer" : null };
        var original = new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = taskId, LandRequestId = request.Id,
            Type = AgentTaskEventType.Landed, IsLandTerminal = true, At = now, Detail = "confirmed publication",
            LandingOperationId = operation.Id, LandingPublication = publication, LandingCleanup = cleanup, LandingMode = operation.Mode };
        var outcome = LandNotificationPayload.Create(request, original, LandNotificationKind.Outcome);
        db.AgentTasks.Add(new AgentTask { Id = taskId, RootTaskId = taskId, Title = "C467 aged evidence", Goal = "monitor fixture",
            WorkingDirectory = Path.GetTempPath(), Status = AgentTaskStatus.Succeeded, CreatedAt = now,
            ActiveLandingId = operation.Id, CurrentLandRequestId = request.Id, LandRequestedAt = heldCleanup ? now : null });
        db.AgentTaskLandings.Add(operation);
        db.AgentTaskLandRequests.Add(request);
        if (!heldCleanup)
        {
            request.TerminalEventId = original.Id;
            request.LandingOperationId = operation.Id;
            db.AgentTaskEvents.Add(original);
            db.AgentTaskLandNotifications.Add(outcome);
            // Receipt aging must use the outcome's immutable evidence, even after later cleanup changes.
            operation.Cleanup = cleanup == LandCleanupStatus.Complete ? LandCleanupStatus.Pending : LandCleanupStatus.Complete;
        }
        await db.SaveChangesAsync();
        foreach (var minutes in new[] { 5, 10 })
        {
            clock.Advance(TimeSpan.FromMinutes(minutes));
            await using var fresh = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            await new AgentTaskLandMonitorService(fresh, clock, Options.Create(new DelegationSettings()), new MockEventBus()).SweepAsync(CancellationToken.None);
        }
        var notes = await db.AgentTaskLandNotifications.AsNoTracking().Where(n => n.RequestId == request.Id && n.Kind == LandNotificationKind.Aged)
            .OrderBy(n => n.CreatedAt).ToListAsync();
        notes.Count.ShouldBe(2);
        foreach (var note in notes)
        {
            var severity = note.CreatedAt == now.AddMinutes(5) ? "Warning" : "Error";
            var detail = heldCleanup
                ? $"{severity}: Land Held; requested {now:O}; no progress since {now:O}; attempt=0; reason=repository_or_source_writer; holder= ()."
                : $"{severity}: outcome receipt unconfirmed; notification={outcome.Id:N}; outcome committed={now:O}; destination=; queue=; state=DestinationUnavailable; error=.";
            note.Body.ShouldBe($"[land {note.Id:N} request={request.Id:N} task={taskId:N} outcome=LandAged]\npublication={publication}; cleanup={cleanup}\n{detail}");
            note.LandingOperationId.ShouldBe(operation.Id);
            var source = await db.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.Id == note.SourceEventId);
            source.LandingOperationId.ShouldBe(operation.Id); source.LandingPublication.ShouldBe(publication);
            source.LandingCleanup.ShouldBe(cleanup); source.LandingMode.ShouldBe(LandOperationMode.CleanupRetry);
        }
    }
    [Test]
    [Arguments(LandRequestState.Queued)]
    [Arguments(LandRequestState.Held)]
    [Arguments(LandRequestState.Running)]
    [Arguments(LandRequestState.NeedsResolution)]
    public async Task C467_V15_ThresholdsUseMeaningfulProgress(LandRequestState state)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var now = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);
        var clock = new FakeTimeProvider(now);
        var taskId = Guid.NewGuid();
        var request = new AgentTaskLandRequest { Id = Guid.NewGuid(), TaskId = taskId, State = state,
            RequestedAt = now, LastProgressAt = now, LastEvaluatedAt = now, ReplyTo = AgentTaskReplyTo.None };
        db.AgentTasks.Add(new AgentTask { Id = taskId, RootTaskId = taskId, Title = "C467 monitor", Goal = "monitor fixture",
            WorkingDirectory = Path.GetTempPath(), Status = AgentTaskStatus.Succeeded, ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = now, LandRequestedAt = now, CurrentLandRequestId = request.Id });
        db.AgentTaskLandRequests.Add(request);
        await db.SaveChangesAsync();
        var service = new AgentTaskLandMonitorService(db, clock, Options.Create(new DelegationSettings()), new MockEventBus());
        clock.Advance(TimeSpan.FromSeconds(299.999));
        await service.SweepAsync(CancellationToken.None);
        (await db.AgentTaskLandNotifications.CountAsync(n => n.RequestId == request.Id)).ShouldBe(0);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await using (var competing = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            await Task.WhenAll(service.SweepAsync(CancellationToken.None), new AgentTaskLandMonitorService(competing, clock,
                Options.Create(new DelegationSettings()), new MockEventBus()).SweepAsync(CancellationToken.None));
        (await db.AgentTaskLandNotifications.CountAsync(n => n.RequestId == request.Id)).ShouldBe(1);
        clock.Advance(TimeSpan.FromSeconds(599.999));
        await service.SweepAsync(CancellationToken.None);
        (await db.AgentTaskLandNotifications.CountAsync(n => n.RequestId == request.Id)).ShouldBe(1);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await service.SweepAsync(CancellationToken.None);
        (await db.AgentTaskLandNotifications.CountAsync(n => n.RequestId == request.Id)).ShouldBe(2);
        await using var restarted = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await new AgentTaskLandMonitorService(restarted, clock, Options.Create(new DelegationSettings()), new MockEventBus()).SweepAsync(CancellationToken.None);
        (await restarted.AgentTaskLandNotifications.CountAsync(n => n.RequestId == request.Id)).ShouldBe(2);
        var saved = await restarted.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id);
        saved.LastProgressAt.ShouldBe(now);
        saved.LastEvaluatedAt.ShouldBe(now.AddMinutes(15));
        saved.Attempt.ShouldBe(0);
    }
}
