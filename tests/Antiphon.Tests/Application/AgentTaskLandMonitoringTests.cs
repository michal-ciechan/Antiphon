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
        var task = new AgentTask { Id = taskId, RootTaskId = taskId, Title = "C467 aged evidence", Goal = "monitor fixture",
            WorkingDirectory = Path.GetTempPath(), Status = AgentTaskStatus.Succeeded, CreatedAt = now,
            CurrentLandRequestId = request.Id, LandRequestedAt = heldCleanup ? now : null };
        db.AgentTasks.Add(task);
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
        task.ActiveLandingId = operation.Id;
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
            note.Body.ShouldBe($"[land {note.Id:N} request={request.Id:N} task={taskId:N} outcome=LandAged]\npublication={publication}; cleanup={cleanup}\nexpected=null; local=null; remote=null; candidate=null\n{detail}");
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

    [Test]
    public async Task C498_MonitorRotatesTokenEveryPass()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var now = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeTimeProvider(now);
        var taskId = Guid.NewGuid();
        var request = new AgentTaskLandRequest
        {
            Id = Guid.NewGuid(), TaskId = taskId, State = LandRequestState.Queued, RequestedAt = now.AddSeconds(-1),
            LastProgressAt = now.AddSeconds(-1), LastEvaluatedAt = now.AddSeconds(-1), ReplyTo = AgentTaskReplyTo.None,
        };
        var before = request.ConcurrencyToken;
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId, RootTaskId = taskId, Title = "C498 monitor", Goal = "monitor fixture",
            WorkingDirectory = Path.GetTempPath(), Status = AgentTaskStatus.Succeeded, ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = now, LandRequestedAt = now.AddSeconds(-1), CurrentLandRequestId = request.Id,
        });
        db.AgentTaskLandRequests.Add(request);
        await db.SaveChangesAsync();
        await new AgentTaskLandMonitorService(db, clock, Options.Create(new DelegationSettings()), new MockEventBus())
            .SweepAsync(CancellationToken.None);
        var after = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id);
        after.ConcurrencyToken.ShouldNotBe(before);
        after.LastEvaluatedAt.ShouldBe(now);
        (await db.AgentTaskLandNotifications.CountAsync(n => n.RequestId == request.Id)).ShouldBe(0);
    }
    /// <summary>
    /// V-19 / G-57, G-58: a DispatchBase note is requestless. It carries the dispatch kind and
    /// dispatch condition key, a null land request, and no <c>request=</c> clause; the land
    /// monitor's Outcome/Aged sweep ignores it entirely; and a linked complete UserPrompt receipt
    /// clears the condition.
    /// </summary>
    [Test]
    [Timeout(120_000)]
    public async Task C508_RequestlessDispatchNotes()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(
            new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var old = DateTime.UtcNow.AddHours(-1);
        var seeded = await SeedDispatchIntentAsync(db, h.SessionId, AgentTaskReplyTo.Session, old);
        await new DispatchBaseWarningIntentService(db, TimeProvider.System)
            .MaterializeAsync(seeded.Intent.Id, CancellationToken.None);
        db.ChangeTracker.Clear();
        var note = await db.AgentTaskLandNotifications.AsNoTracking()
            .SingleAsync(n => n.Id == seeded.Intent.NotificationId);
        note.Kind.ShouldBe(LandNotificationKind.DispatchBase);
        note.RequestId.ShouldBeNull();
        note.SourceEventId.ShouldBe(seeded.Intent.Id);

        var notifier = new AgentTaskLandNotificationService(
            db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
        await notifier.ReconcileAsync(note.Id, CancellationToken.None);

        async Task<AttentionDto> ReadAsync()
        {
            await using var fresh = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            return await new AttentionService(fresh, h.Runner, Options.Create(new SupervisionSettings()),
                Options.Create(new DelegationSettings()), TimeProvider.System,
                NullLogger<AttentionService>.Instance).GetAsync(CancellationToken.None);
        }

        var item = (await ReadAsync()).Items.Single(i => i.LandNotificationId == note.Id);
        item.Kind.ShouldBe(AttentionKind.DispatchWarningUnconfirmed);
        item.ConditionKey.ShouldBe($"dispatch:{note.Id:N}:receipt");
        item.LandRequestId.ShouldBeNull();
        item.TaskId.ShouldBe(seeded.Task.Id);
        item.SessionId.ShouldBe(h.SessionId);
        item.Actions.ShouldBe([AttentionAction.OpenDrawer]);
        item.Headline.ShouldStartWith("Dispatch warning notification:");
        item.Evidence.ShouldNotContain("request=");
        item.Evidence.ShouldContain($"notification={note.Id:N}");
        ((int)AttentionKind.DispatchWarningUnconfirmed).ShouldBe(40);

        // The land monitor owns land REQUESTS. A requestless dispatch note must not age through it.
        await new AgentTaskLandMonitorService(db, TimeProvider.System,
            Options.Create(new DelegationSettings()), new MockEventBus()).SweepAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
        (await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == seeded.Task.Id)).ShouldBe(1);
        (await db.AgentTaskLandNotifications.CountAsync(n => n.Kind == LandNotificationKind.Aged)).ShouldBe(0);

        // The linked complete receipt — not a Sent flag — clears the condition.
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId);
        queued.DeliveryAttempts = 1;
        queued.Status = QueuedMessageStatus.Sent;
        queued.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1);
        queued.LastDeliveryBaselineSequence = 10;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 11,
            Kind = TranscriptKinds.UserPrompt, Text = queued.Body,
            Timestamp = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        await notifier.ReconcileAsync(note.Id, CancellationToken.None);
        db.ChangeTracker.Clear();
        (await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id))
            .ConfirmedAt.ShouldNotBeNull();
        (await ReadAsync()).Items.ShouldNotContain(i => i.ConditionKey == $"dispatch:{note.Id:N}:receipt");
    }

    /// <summary>
    /// V-28 / G-95, G-99, G-100, G-101, G-102: a captured but not-yet-materialized warning is
    /// already visible, on the threshold edges, with no note to point at yet.
    /// </summary>
    [Test]
    [Timeout(120_000)]
    [Arguments(299.999, null, false, null)]
    [Arguments(300.0, null, true, "Warning")]
    [Arguments(899.999, null, true, "Warning")]
    [Arguments(900.0, null, true, "Error")]
    [Arguments(10.0, "intent_materialize_failed:TimeoutException", true, "Error")]
    public async Task C508_PendingIntentAttention(
        double ageSeconds, string? errorCode, bool visible, string? severity)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(
            new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var now = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeTimeProvider(now);
        var createdAt = now.AddSeconds(-ageSeconds);
        var seeded = await SeedDispatchIntentAsync(db, h.SessionId, AgentTaskReplyTo.Session, createdAt);
        if (errorCode is not null)
        {
            seeded.Intent.LastErrorCode = errorCode;
            seeded.Intent.LastErrorAt = createdAt;
            await db.SaveChangesAsync();
        }

        var items = await ReadItemsAsync(schema, h, clock);
        var key = $"dispatch:{seeded.Intent.NotificationId:N}:receipt";
        var match = items.SingleOrDefault(i => i.ConditionKey == key);
        if (!visible)
        {
            match.ShouldBeNull();
            return;
        }

        match.ShouldNotBeNull();
        match.Kind.ShouldBe(AttentionKind.DispatchWarningUnconfirmed);
        match.Severity.ShouldBe(severity == "Error" ? AlertSeverity.Error : AlertSeverity.Warning);
        // No note row yet: the item must not invent a notification reference.
        match.LandNotificationId.ShouldBeNull();
        match.LandRequestId.ShouldBeNull();
        match.MessageId.ShouldBeNull();
        match.TaskId.ShouldBe(seeded.Task.Id);
        match.Headline.ShouldBe("Dispatch warning awaiting materialization");
        match.Evidence.ShouldContain($"notification={seeded.Intent.NotificationId:N}");
        match.Evidence.ShouldNotContain("request=");

        // ReplyTo None owes nothing and is never a pending-receipt condition.
        var silent = await SeedDispatchIntentAsync(db, null, AgentTaskReplyTo.None, createdAt);
        // A missing destination is still owed, and stays visible.
        var orphan = await SeedDispatchIntentAsync(db, null, AgentTaskReplyTo.Session, createdAt);
        var second = await ReadItemsAsync(schema, h, clock);
        second.ShouldNotContain(i => i.ConditionKey == $"dispatch:{silent.Intent.NotificationId:N}:receipt");
        second.ShouldContain(i => i.ConditionKey == $"dispatch:{orphan.Intent.NotificationId:N}:receipt");
    }

    /// <summary>
    /// V-28 / G-96, G-97, G-98: the dispatch condition is one condition across the projection
    /// boundary, keyed by the exact preallocated NotificationId. The projection is committed
    /// between the two halves of the attention read in BOTH orders, and a newer unrelated note on
    /// the same task must keep its own separate condition rather than masking or being masked.
    /// </summary>
    [Test]
    [Timeout(120_000)]
    [Arguments(true)]
    [Arguments(false)]
    public async Task C508_IntentAttentionHandoff(bool projectBeforeRead)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(
            new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var now = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeTimeProvider(now);
        var createdAt = now.AddSeconds(-1000);
        var seeded = await SeedDispatchIntentAsync(db, h.SessionId, AgentTaskReplyTo.Session, createdAt);
        var key = $"dispatch:{seeded.Intent.NotificationId:N}:receipt";

        // An unrelated, newer dispatch obligation on the SAME task. Suppression is by exact
        // NotificationId, so this must never stand in for (or hide) the row above.
        var unrelated = await SeedDispatchIntentAsync(
            db, h.SessionId, AgentTaskReplyTo.Session, createdAt.AddSeconds(1),
            task: seeded.Task, warningKey: DispatchBaseNotificationPayload.MismatchKey);
        var unrelatedKey = $"dispatch:{unrelated.Intent.NotificationId:N}:receipt";
        unrelatedKey.ShouldNotBe(key);

        async Task ProjectAsync()
        {
            await using var projector = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            await new DispatchBaseWarningIntentService(projector, clock)
                .MaterializeAsync(seeded.Intent.Id, CancellationToken.None);
        }

        if (projectBeforeRead)
            await ProjectAsync();

        var before = await ReadItemsAsync(schema, h, clock);
        before.Count(i => i.ConditionKey == key).ShouldBe(1);
        before.Count(i => i.ConditionKey == unrelatedKey).ShouldBe(1);
        if (projectBeforeRead)
        {
            before.Single(i => i.ConditionKey == key).LandNotificationId
                .ShouldBe(seeded.Intent.NotificationId);
        }
        else
        {
            before.Single(i => i.ConditionKey == key).LandNotificationId.ShouldBeNull();
            await ProjectAsync();
        }

        // Whatever order the commit landed in, the same single condition survives it, and now
        // carries the real notification id.
        var after = await ReadItemsAsync(schema, h, clock);
        after.Count(i => i.ConditionKey == key).ShouldBe(1);
        after.Single(i => i.ConditionKey == key).LandNotificationId.ShouldBe(seeded.Intent.NotificationId);
        after.Count(i => i.ConditionKey == unrelatedKey).ShouldBe(1);
        after.Single(i => i.ConditionKey == unrelatedKey).LandNotificationId.ShouldBeNull();
    }

    private static async Task<List<AttentionItemDto>> ReadItemsAsync(
        IsolatedTestSchema schema, BridgeQueueHarness h, TimeProvider clock)
    {
        await using var fresh = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var result = await new AttentionService(fresh, h.Runner, Options.Create(new SupervisionSettings()),
            Options.Create(new DelegationSettings()), clock,
            NullLogger<AttentionService>.Instance).GetAsync(CancellationToken.None);
        return [.. result.Items];
    }

    /// <summary>
    /// A genuine producer-shaped intent: a real Dispatched event on a real task, captured through
    /// the production capture path rather than an invented row.
    /// </summary>
    private static async Task<(AgentTask Task, AgentTaskDispatchWarningIntent Intent)> SeedDispatchIntentAsync(
        AppDbContext db,
        Guid? session,
        AgentTaskReplyTo replyTo,
        DateTime createdAt,
        AgentTask? task = null,
        string? warningKey = null)
    {
        if (task is null)
        {
            var taskId = Guid.NewGuid();
            task = new AgentTask
            {
                Id = taskId, RootTaskId = taskId, Title = "C508 dispatch warning", Goal = "dispatch fixture",
                WorkingDirectory = Path.GetTempPath(), Status = AgentTaskStatus.Dispatched,
                ReplyTo = replyTo, ParentSessionId = session, CreatedAt = createdAt,
            };
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
        }

        var dispatchEvent = new AgentTaskEvent
        {
            Id = Guid.NewGuid(), AgentTaskId = task.Id, Type = AgentTaskEventType.Dispatched,
            Detail = "Dispatched to agent 'fixture'", At = createdAt,
        };
        db.AgentTaskEvents.Add(dispatchEvent);
        await db.SaveChangesAsync();

        var key = warningKey ?? DispatchBaseNotificationPayload.SiblingKey(Guid.NewGuid());
        var ids = await new DispatchBaseWarningIntentService(db, TimeProvider.System).CaptureAsync(
            task, dispatchEvent,
            [new DispatchWarningDraft(key, $"CARD-0508's kept branch feat/card-task-{key[^8..]} is not contained.")],
            CancellationToken.None);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var intent = await db.AgentTaskDispatchWarningIntents.AsNoTracking().SingleAsync(i => i.Id == ids[0]);
        return (task, intent);
    }
}
