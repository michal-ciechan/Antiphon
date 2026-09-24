using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0641 D-2. Receipt of a non-legacy Held, Aged, Conflict or Outcome note accepts a complete
/// submitted QueuedUserPrompt. Housekeeping records and every other kind stay unconfirmed until a
/// UserPrompt. These are matcher cases on the existing notification seed, not a second land producer.
/// </summary>
[Category("Integration")]
public sealed class AgentTaskLandQueuedReceiptTests
{
    private static readonly DateTime HistoricalPromptAt = new(2026, 9, 23, 22, 29, 34, 702, DateTimeKind.Utc);

    [Test]
    [Timeout(180_000)]
    [Arguments("Held")]
    [Arguments("Aged")]
    [Arguments("Conflict")]
    [Arguments("Outcome")]
    public async Task C641_Submitted_queued_prompt_confirms_land_lifecycle_note(string kind)
    {
        await using var fx = await ReceiptFixture.OpenAsync(Enum.Parse<LandNotificationKind>(kind));
        await fx.MarkAttemptedAsync(attempts: 1, floor: 10, started: DateTime.UtcNow);
        await fx.AddPromptAsync(TranscriptKinds.QueuedUserPrompt, fx.Note.Body, sequence: 11, timestamp: DateTime.UtcNow);
        await fx.ReconcileAsync();
        var saved = await fx.ReloadAsync();
        saved.State.ShouldBe(LandNotificationState.Confirmed, "missing receipt");
        saved.ConfirmingPromptSequence.ShouldBe(11L);
        saved.LastErrorCode.ShouldBeNull();
        fx.Harness.Adapter.Inputs.ShouldBeEmpty("receipt reconciliation must never type");
    }

    [Test]
    [Timeout(180_000)]
    public async Task C641_Historical_1cef_shape_confirms_without_retyping_and_stops_escalation()
    {
        await using var fx = await ReceiptFixture.OpenAsync(LandNotificationKind.Outcome);
        var row = await fx.QueueRowAsync();
        row.Body.ShouldBe(fx.Note.Body);
        fx.Note.CreatedAt = HistoricalPromptAt;
        await fx.Db.SaveChangesAsync();
        await fx.MarkAttemptedAsync(attempts: 1, floor: 73258, started: HistoricalPromptAt);
        var wrapped = "<pasted_content name=\"c641-historical\">\n" + fx.Note.Body + "\n</pasted_content>";
        PromptSubmissionMatch.IsCompleteIn(fx.Note.Body, wrapped).ShouldBeTrue();
        await fx.AddPromptAsync(TranscriptKinds.QueuedUserPrompt, wrapped, sequence: 73264, timestamp: HistoricalPromptAt);
        await fx.ReconcileAsync();

        var saved = await fx.ReloadAsync();
        saved.State.ShouldBe(LandNotificationState.Confirmed, "missing receipt");
        saved.ConfirmingPromptSequence.ShouldBe(73264L);
        saved.LastErrorCode.ShouldBeNull();

        var promptsBefore = await fx.Db.TranscriptEntries.CountAsync(p => p.AgentSessionId == fx.Harness.SessionId);
        await fx.ReconcileAsync();
        (await fx.Db.TranscriptEntries.CountAsync(p => p.AgentSessionId == fx.Harness.SessionId)).ShouldBe(promptsBefore);
        (await fx.Db.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == fx.Note.Id)).ShouldBe(1);
        fx.Harness.Adapter.Inputs.ShouldBeEmpty("a second reconcile must not retype");

        (DateTime.UtcNow - saved.CreatedAt).TotalSeconds.ShouldBeGreaterThan(new DelegationSettings().LandErrorSeconds);
        await using var monitorDb = new AppDbContext(TestDbFixture.CreateDbContextOptions(fx.Schema.ConnectionString));
        await new AgentTaskLandMonitorService(monitorDb, TimeProvider.System, Options.Create(new DelegationSettings()), new MockEventBus())
            .SweepAsync(CancellationToken.None);
        await using var observed = new AppDbContext(TestDbFixture.CreateDbContextOptions(fx.Schema.ConnectionString));
        var after = await observed.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == fx.Note.Id);
        after.State.ShouldBe(LandNotificationState.Confirmed);
        after.ErrorAt.ShouldBeNull();
        after.ConfirmingPromptSequence.ShouldBe(73264L);
        (await observed.AgentTaskEvents.CountAsync(e => e.AgentTaskId == fx.Note.TaskId && e.Type == AgentTaskEventType.LandAged)).ShouldBe(0);
        (await observed.AgentTaskLandNotifications.CountAsync(n => n.TaskId == fx.Note.TaskId && n.Kind == LandNotificationKind.Aged)).ShouldBe(0);
    }

    [Test]
    [Timeout(180_000)]
    [Arguments("enqueue")]
    [Arguments("dequeue")]
    [Arguments("remove")]
    [Arguments("head-only")]
    [Arguments("head-tail-splice")]
    [Arguments("wrong-session")]
    [Arguments("wrong-identity")]
    [Arguments("unattempted")]
    public async Task C641_Receipt_rejects_non_submission_or_incomplete_evidence(string evidence)
    {
        await using var fx = await ReceiptFixture.OpenAsync(LandNotificationKind.Outcome);
        var started = DateTime.UtcNow;
        await fx.MarkAttemptedAsync(attempts: evidence == "unattempted" ? 0 : 1, floor: 10, started: started);
        var session = fx.Harness.SessionId;
        if (evidence == "wrong-session")
        {
            session = Guid.NewGuid();
            fx.Db.AgentSessions.Add(new AgentSession { Id = session, CreatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow });
            await fx.Db.SaveChangesAsync();
        }

        var body = fx.Note.Body;
        body.Length.ShouldBeGreaterThan(300);
        var text = evidence switch
        {
            "head-only" => body[..200],
            "head-tail-splice" => body[..200] + body[^100..],
            "wrong-identity" => body.Replace(fx.Note.Id.ToString("N"), Guid.NewGuid().ToString("N"), StringComparison.Ordinal),
            _ => body,
        };
        var kind = evidence switch
        {
            "enqueue" => TranscriptKinds.QueueEnqueue,
            "dequeue" => TranscriptKinds.QueueDequeue,
            "remove" => TranscriptKinds.QueueRemove,
            _ => TranscriptKinds.QueuedUserPrompt,
        };
        await fx.AddPromptAsync(kind, text, sequence: 11, timestamp: DateTime.UtcNow, sessionId: session);
        await fx.ReconcileAsync();
        var saved = await fx.ReloadAsync();
        saved.ConfirmedAt.ShouldBeNull();
        saved.ConfirmingPromptSequence.ShouldBeNull();
        saved.State.ShouldNotBe(LandNotificationState.Confirmed);
        fx.Harness.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    [Timeout(180_000)]
    [Arguments("old-sequence")]
    [Arguments("equal-sequence")]
    [Arguments("old-time")]
    [Arguments("no-floor")]
    public async Task C641_Receipt_keeps_sequence_and_timestamp_floors(string evidence)
    {
        await using var fx = await ReceiptFixture.OpenAsync(LandNotificationKind.Outcome);
        var started = DateTime.UtcNow;
        long? floor = evidence is "old-time" or "no-floor" ? null : 10;
        DateTime? startedAt = evidence == "no-floor" ? null : started;
        await fx.MarkAttemptedAsync(attempts: 1, floor: floor, started: startedAt);
        var sequence = evidence switch
        {
            "old-sequence" => 9L,
            "equal-sequence" => 10L,
            _ => 11L,
        };
        var timestamp = evidence == "old-time" ? started.AddHours(-1) : DateTime.UtcNow;
        await fx.AddPromptAsync(TranscriptKinds.QueuedUserPrompt, fx.Note.Body, sequence, timestamp);
        await fx.ReconcileAsync();
        var saved = await fx.ReloadAsync();
        saved.ConfirmedAt.ShouldBeNull();
        saved.ConfirmingPromptSequence.ShouldBeNull();
        saved.State.ShouldNotBe(LandNotificationState.Confirmed);
    }

    [Test]
    [Timeout(180_000)]
    [Arguments("DispatchBase")]
    [Arguments("DeliveryFailure")]
    [Arguments("TaskCompletion")]
    [Arguments("LegacyCheckNote")]
    [Arguments("legacy-Outcome")]
    public async Task C641_Other_notification_kinds_keep_UserPrompt_contract(string kind)
    {
        var legacyOutcome = kind == "legacy-Outcome";
        await using var fx = await ReceiptFixture.OpenAsync(
            legacyOutcome ? LandNotificationKind.Outcome : Enum.Parse<LandNotificationKind>(kind),
            legacyAfterEnqueue: legacyOutcome);
        if (legacyOutcome)
            fx.Note.IsLegacy.ShouldBeTrue();
        await fx.MarkAttemptedAsync(attempts: 1, floor: 10, started: DateTime.UtcNow);
        await fx.AddPromptAsync(TranscriptKinds.QueuedUserPrompt, fx.Note.Body, sequence: 11, timestamp: DateTime.UtcNow);
        await fx.ReconcileAsync();
        var rejected = await fx.ReloadAsync();
        rejected.ConfirmedAt.ShouldBeNull();
        rejected.ConfirmingPromptSequence.ShouldBeNull();
        rejected.State.ShouldNotBe(LandNotificationState.Confirmed);

        await fx.AddPromptAsync(TranscriptKinds.UserPrompt, fx.Note.Body, sequence: 12, timestamp: DateTime.UtcNow);
        await fx.ReconcileAsync();
        var saved = await fx.ReloadAsync();
        saved.State.ShouldBe(LandNotificationState.Confirmed);
        saved.ConfirmingPromptSequence.ShouldBe(12L);
        saved.LastErrorCode.ShouldBeNull();
        fx.Harness.Adapter.Inputs.ShouldBeEmpty();
    }

    private sealed class ReceiptFixture : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        private readonly AgentTaskLandNotificationService _service;
        public BridgeQueueHarness Harness { get; }
        public AppDbContext Db { get; }
        public AgentTaskLandNotification Note { get; }
        public IsolatedTestSchema Schema => _schema;

        private ReceiptFixture(IsolatedTestSchema schema, BridgeQueueHarness harness, AppDbContext db,
            AgentTaskLandNotificationService service, AgentTaskLandNotification note)
        {
            _schema = schema;
            Harness = harness;
            Db = db;
            _service = service;
            Note = note;
        }

        public static async Task<ReceiptFixture> OpenAsync(LandNotificationKind kind, bool legacyAfterEnqueue = false)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var harness = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
            var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            try
            {
                var note = await AgentTaskLandReceiptTests.SeedAsync(db, harness.SessionId);
                if (note.Kind != kind)
                {
                    note.Kind = kind;
                    await db.SaveChangesAsync();
                }

                var service = new AgentTaskLandNotificationService(db, harness.Queue, new CompletionNoteFlushQueue(), harness.Runtime, TimeProvider.System);
                await service.ReconcileAsync(note.Id, CancellationToken.None);
                await db.Entry(note).ReloadAsync();
                note.QueueMessageId.ShouldNotBeNull();
                if (legacyAfterEnqueue)
                {
                    note.IsLegacy = true;
                    await db.SaveChangesAsync();
                }

                return new ReceiptFixture(schema, harness, db, service, note);
            }
            catch
            {
                await db.DisposeAsync();
                await harness.DisposeAsync();
                await schema.DisposeAsync();
                throw;
            }
        }

        public async Task MarkAttemptedAsync(int attempts, long? floor, DateTime? started)
        {
            var row = await Db.SessionQueuedMessages.SingleAsync(m => m.Id == Note.QueueMessageId);
            row.DeliveryAttempts = attempts;
            row.Status = attempts > 0 ? QueuedMessageStatus.Sent : QueuedMessageStatus.Pending;
            row.LastDeliveryBaselineSequence = floor;
            row.LastDeliveryStartedAt = started;
            row.DeliveryVerdict = null;
            await Db.SaveChangesAsync();
        }

        public async Task<SessionQueuedMessage> QueueRowAsync() =>
            await Db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == Note.QueueMessageId);

        public async Task AddPromptAsync(string kind, string text, long sequence, DateTime timestamp, Guid? sessionId = null)
        {
            Db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId ?? Harness.SessionId,
                Sequence = sequence,
                Kind = kind,
                Text = text,
                Timestamp = timestamp,
                CreatedAt = DateTime.UtcNow,
            });
            await Db.SaveChangesAsync();
        }

        public Task ReconcileAsync() => _service.ReconcileAsync(Note.Id, CancellationToken.None);

        public async Task<AgentTaskLandNotification> ReloadAsync()
        {
            await Db.Entry(Note).ReloadAsync();
            return Note;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Harness.DisposeAsync();
            await _schema.DisposeAsync();
        }
    }
}
