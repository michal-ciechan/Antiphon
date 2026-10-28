using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1591. A withheld Claude note is named, charged once when the withhold is durable,
/// and readable without typing. Adopted idle, an operator draft, a dialog, and Working
/// still write no bytes.
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class PendingNoteWithholdTests
{
    private const string Modal = "Which option?\n❯ 1. First\n2. Second\nEnter to select · Esc to cancel";

    [Test]
    [Timeout(180_000)]
    public async Task C1591_NamesWithholdsChargesOnceAndAlerts(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var harness = await ClaudeDialogFixture.CreateBridgeAsync(
            new BridgeQueueHarness.HarnessOptions { ConnectionString = schema.ConnectionString });
        await harness.InsertStoreVisibleTurnAsync("earlier operator prompt", "earlier reply");
        long floor;
        DateTime generation;
        await using (var db = harness.CreateObserverContext())
        {
            floor = await db.TranscriptEntries.Where(t => t.AgentSessionId == harness.SessionId)
                .MaxAsync(t => t.Sequence, ct);
            generation = SessionGeneration.Normalize(await db.AgentSessions.Where(s => s.Id == harness.SessionId)
                .Select(s => s.StartedAt).SingleAsync(ct));
        }

        harness.Adapter.DialogAdopted = true;
        var adopted = await ExpectWithheldAsync(
            harness, "adopted notice", DialogDeliveryAdmission.AdoptedSession, 1, ct);
        await AssertFloorAsync(harness, adopted.Id, floor, generation, ct);
        await harness.Queue.OnTurnEndAsync(harness.SessionId, ct);
        (await ReloadAsync(harness, adopted.Id, ct)).DeliveryAttempts.ShouldBe(1);
        var sent = await harness.Queue.SendNowAsync(harness.SessionId, adopted.Id, ct);
        sent.ModalBlocked.ShouldBeTrue();
        sent.ModalBlockedReason.ShouldBe(DialogDeliveryAdmission.AdoptedSession);
        sent.Messages.Single(m => m.Id == adopted.Id).DeliveryAttempts.ShouldBe(1);
        harness.Adapter.Inputs.ShouldBeEmpty();
        harness.Adapter.SubmittedBodies.ShouldBeEmpty();
        harness.Adapter.ConditionalInputs.ShouldBeEmpty();

        harness.Adapter.RenderedScreenOverride = ClaudeDialogFixture.ComposerScreen(draft: "Try \"ship it\"");
        var draft = await ExpectWithheldAsync(
            harness, "draft notice", DialogDeliveryAdmission.AdoptedSession, 1, ct);
        draft.Body.ShouldBe("draft notice");

        harness.Adapter.DialogAdopted = false;
        harness.Adapter.DialogInputUncertain = true;
        harness.Adapter.RenderedScreenOverride = null;
        await ExpectWithheldAsync(
            harness, "uncertain notice", DialogDeliveryAdmission.InputUncertain, 1, ct);

        harness.Adapter.DialogInputUncertain = false;
        var bound = harness.Adapter.AcceptedStartedAt;
        harness.Adapter.AcceptedStartedAt = bound!.Value.AddMinutes(-10);
        await ExpectWithheldAsync(
            harness, "stale notice", DialogDeliveryAdmission.GenerationMismatch, 1, ct);
        harness.Adapter.AcceptedStartedAt = bound;

        harness.Adapter.RenderedScreenOverride = "partial frame";
        await ExpectWithheldAsync(
            harness, "partial notice", DialogDeliveryAdmission.ScreenUnknown, 1, ct);

        harness.Adapter.DialogAdopted = true;
        harness.Adapter.RenderedScreenOverride = Modal;
        var dialog = await ExpectWithheldAsync(
            harness, "dialog notice", DialogDeliveryAdmission.BlocksInput, 0, ct);
        await harness.Queue.OnTurnEndAsync(harness.SessionId, ct);
        (await ReloadAsync(harness, dialog.Id, ct)).DeliveryAttempts.ShouldBe(0);
        (await ReloadAsync(harness, adopted.Id, ct)).DeliveryAttempts.ShouldBe(1);

        harness.Adapter.DialogGuardEnabled = false;
        harness.Adapter.DialogAdopted = false;
        harness.Adapter.RenderedScreenOverride = null;
        var missing = await ExpectWithheldAsync(
            harness, "missing notice", DialogDeliveryAdmission.SnapshotUnavailable, 0, ct);
        (await ReloadAsync(harness, dialog.Id, ct)).DeliveryAttempts.ShouldBe(0);
        (await ReloadAsync(harness, missing.Id, ct)).LastDeliveryStartedAt.ShouldBeNull();

        harness.Adapter.Inputs.ShouldBeEmpty();
        harness.Adapter.SubmittedBodies.ShouldBeEmpty();
        harness.Adapter.ConditionalInputs.ShouldBeEmpty();

        var page = await harness.Queue.ListPendingNotesAsync(harness.SessionId, 0, 1, ct);
        page.Notes.Count.ShouldBe(1);
        page.ModalBlocked.ShouldBeTrue();
        page.WithholdReason.ShouldBe(DialogDeliveryAdmission.SnapshotUnavailable);
        page.NextSince.ShouldBe(page.Notes[0].Sequence);
        page.Notes[0].Body.ShouldBe("adopted notice");
        var rest = await harness.Queue.ListPendingNotesAsync(harness.SessionId, page.NextSince, 50, ct);
        rest.Notes.ShouldNotContain(n => n.Sequence == page.Notes[0].Sequence);
        rest.Notes.ShouldContain(n => n.Body == "dialog notice" && n.DeliveryAttempts == 0);
        rest.Notes.ShouldContain(n => n.Body == "uncertain notice" && n.DeliveryAttempts == 1);
        var wide = await harness.Queue.ListPendingNotesAsync(harness.SessionId, -1, 0, ct);
        wide.Since.ShouldBe(0);
        wide.Notes.Count.ShouldBeGreaterThan(1);
        wide.Notes.Count.ShouldBeLessThanOrEqualTo(SessionMessageQueueService.PendingNotePageDefault);
        await Should.ThrowAsync<NotFoundException>(() =>
            harness.Queue.ListPendingNotesAsync(Guid.NewGuid(), 0, 10, ct));

        var oldId = await harness.SeedPendingMessageAsync(
            "aged withheld note", createdAtUtc: harness.Now.AddMinutes(-20));
        await harness.SeedPendingMessageAsync(
            "young withheld note", createdAtUtc: harness.Now.AddMinutes(-5));
        await harness.SeedPendingMessageAsync(
            "parked withheld note", createdAtUtc: harness.Now.AddMinutes(-20), deliveryAttempts: 3);

        await using var observer = harness.CreateObserverContext();
        var feed = await AttentionServiceTests.BuildService(
            new AttentionServiceTests.FakeRunnerClient(), db: observer).GetAsync(ct);
        var aged = feed.Items.Where(i => i.Kind == AttentionKind.PendingNotesAged).ToList();
        aged.Count.ShouldBe(1);
        aged[0].SessionId.ShouldBe(harness.SessionId);
        aged[0].MessageId.ShouldBe(oldId);
        aged[0].Severity.ShouldBe(AlertSeverity.Warning);
        aged[0].Actions.ShouldBe([AttentionAction.OpenDrawer]);
        aged[0].ConditionKey.ShouldBe($"pending-notes-aged:{harness.SessionId:N}");
        aged[0].Title.ShouldContain("1 pending note");
        aged[0].Evidence.ShouldContain("nothing was flushed or sent");

        var silenced = await new AttentionService(
            observer,
            new AttentionServiceTests.FakeRunnerClient(),
            Options.Create(new SupervisionSettings
            {
                DeliveryVerification = new DeliveryVerificationSettings { PendingNoteAlertAgeSeconds = 0 },
            }),
            Options.Create(new DelegationSettings()),
            TimeProvider.System,
            NullLogger<AttentionService>.Instance).GetAsync(ct);
        silenced.Items.ShouldNotContain(i => i.Kind == AttentionKind.PendingNotesAged);

        var belowMinimum = await new AttentionService(
            observer,
            new AttentionServiceTests.FakeRunnerClient(),
            Options.Create(new SupervisionSettings
            {
                DeliveryVerification = new DeliveryVerificationSettings { PendingNoteAlertMinimumCount = 2 },
            }),
            Options.Create(new DelegationSettings()),
            TimeProvider.System,
            NullLogger<AttentionService>.Instance).GetAsync(ct);
        belowMinimum.Items.ShouldNotContain(i => i.Kind == AttentionKind.PendingNotesAged);
    }

    [Test]
    [Timeout(180_000)]
    public async Task C1591_ExpiredOptionalBriefCancelsAfterWithhold(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var harness = await ClaudeDialogFixture.CreateBridgeAsync(
            new BridgeQueueHarness.HarnessOptions { ConnectionString = schema.ConnectionString });
        await harness.InsertStoreVisibleTurnAsync("earlier operator prompt", "earlier reply");
        var taskId = Guid.NewGuid();
        await using (var db = harness.CreateObserverContext())
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "optional brief",
                Goal = "expire before a late type",
                Role = AgentTaskRole.Distill,
                Status = AgentTaskStatus.Dispatched,
                CreatedAt = harness.Now,
            });
            await db.SaveChangesAsync(ct);
        }

        harness.Adapter.DialogAdopted = true;
        const string body = "optional brief that must expire";
        await harness.Queue.EnqueueAsync(
            harness.SessionId, body, MessageSendMode.WhenIdle, ct,
            executionDeadlineAt: harness.Now.AddMinutes(-1), executionTaskId: taskId);
        harness.Adapter.Inputs.ShouldBeEmpty();
        harness.Adapter.SubmittedBodies.ShouldBeEmpty();

        harness.Adapter.DialogAdopted = false;
        harness.Adapter.RenderedScreenOverride = null;
        await harness.Queue.FlushSessionAsync(harness.SessionId, ct);

        await using var verify = harness.CreateObserverContext();
        var row = await verify.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == harness.SessionId && m.Body == body, ct);
        row.Status.ShouldBe(QueuedMessageStatus.Canceled);
        row.DeliveryAttempts.ShouldBe(0);
        row.LastDeliveryStartedAt.ShouldBeNull();
        row.LastDeliveryBaselineSequence.ShouldBeNull();
        var task = await verify.AgentTasks.SingleAsync(t => t.Id == taskId, ct);
        task.Status.ShouldBe(AgentTaskStatus.Canceled);
        task.FailureReason.ShouldBe("Optional work expired before execution.");
        harness.Adapter.Inputs.ShouldBeEmpty();
        harness.Adapter.SubmittedBodies.ShouldBeEmpty();
    }

    [Test]
    [Timeout(180_000)]
    public async Task C1591_WithheldNotesStayTransferable(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var harness = await ClaudeDialogFixture.CreateBridgeAsync(
            new BridgeQueueHarness.HarnessOptions { ConnectionString = schema.ConnectionString });
        await harness.InsertStoreVisibleTurnAsync("earlier operator prompt", "earlier reply");
        harness.Adapter.DialogAdopted = true;
        var queued = await harness.Queue.EnqueueAsync(
            harness.SessionId, "ordinary withheld note", MessageSendMode.WhenIdle, ct);
        var id = queued.Messages.Single(m => m.Body == "ordinary withheld note").Id;
        var row = await ReloadAsync(harness, id, ct);
        StandingQueueSwitchPolicy.NeverAttempted(row).ShouldBeTrue();
        new StandingRestartEscalationPolicy(new SupervisionSettings(), harness.Delegation)
            .CanTransfer(row).ShouldBeTrue();
        row.DeliveryAttempts.ShouldBe(0);
        row.LastDeliveryStartedAt.ShouldBeNull();
        row.LastDeliveryBaselineSequence.ShouldBeNull();
        row.DeliveryVerdict.ShouldBeNull();
        harness.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    [Timeout(180_000)]
    public async Task C1591_ResumeNoteIsNotFailedAfterWithhold(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var harness = await ClaudeDialogFixture.CreateBridgeAsync(
            new BridgeQueueHarness.HarnessOptions { ConnectionString = schema.ConnectionString });
        await harness.InsertStoreVisibleTurnAsync("earlier operator prompt", "earlier reply");
        DateTime generation;
        await using (var db = harness.CreateObserverContext())
        {
            await db.AgentSessions.Where(s => s.Id == harness.SessionId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.StandingAgentId, harness.AgentId), ct);
            generation = SessionGeneration.Normalize(await db.AgentSessions.Where(s => s.Id == harness.SessionId)
                .Select(s => s.StartedAt).SingleAsync(ct));
        }

        harness.Runner.TranscriptPull = (id, _) => Task.FromResult(new SessionRunnerTranscriptDto(
            id, [], 0, AcceptedStartedAt: generation,
            Readiness: new SessionRunnerTranscriptReadiness("Ready", "binding", 100, 100)));
        var bound = harness.Adapter.AcceptedStartedAt;
        harness.Adapter.AcceptedStartedAt = bound!.Value.AddMinutes(-10);
        var noteId = Guid.Empty;
        await harness.Queue.EnqueueAsync(
            harness.SessionId, "Restart obligation for this launch: resume the standing work.",
            MessageSendMode.WhenIdle, ct, origin: QueuedMessageOrigin.System, deliverIfIdle: false,
            runtimeActionKey: StandingResumeNote.Key(generation), onCreated: created => noteId = created);
        await harness.Queue.FlushSessionAsync(harness.SessionId, ct);
        var withheld = await ReloadAsync(harness, noteId, ct);
        withheld.Status.ShouldBe(QueuedMessageStatus.Pending);
        harness.Adapter.Inputs.ShouldBeEmpty();
        (await harness.Queue.GetQueueAsync(harness.SessionId, ct)).ModalBlockedReason
            .ShouldBe(DialogDeliveryAdmission.GenerationMismatch);

        harness.Adapter.AcceptedStartedAt = bound;
        harness.Adapter.DialogAdopted = false;
        harness.Adapter.DialogInputUncertain = false;
        harness.Adapter.RenderedScreenOverride = null;
        RecordRealPrompts(harness);
        await harness.Queue.FlushSessionAsync(harness.SessionId, ct);

        var row = await ReloadAsync(harness, noteId, ct);
        row.DeliveryVerdict.ShouldNotBe(DeliveryVerdict.NoTranscriptRecord);
        row.DeliveryAttempts.ShouldBe(1);
        row.Status.ShouldBe(QueuedMessageStatus.Sent);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        harness.Adapter.SubmittedBodies.Count.ShouldBe(1);
        await using var incidents = harness.CreateObserverContext();
        (await incidents.AgentIncidents.CountAsync(i => i.AgentId == harness.AgentId
            && i.Kind == AgentIncidentKind.DeliveryVerificationFailed, ct)).ShouldBe(0);
    }

    [Test]
    [Timeout(180_000)]
    public async Task C1591_QuotedHeadDoesNotConfirmOrParkAWithheldNote(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var harness = await ClaudeDialogFixture.CreateBridgeAsync(
            new BridgeQueueHarness.HarnessOptions { ConnectionString = schema.ConnectionString });
        await harness.InsertStoreVisibleTurnAsync("earlier operator prompt", "earlier reply");
        var body = new string('A', PromptSubmissionMatch.MatchWindowChars) + " withheld tail not in the quote";
        harness.Adapter.DialogAdopted = true;
        var queued = await harness.Queue.EnqueueAsync(harness.SessionId, body, MessageSendMode.WhenIdle, ct);
        var id = queued.Messages.Single(m => m.Body == body).Id;
        await harness.InsertTranscriptEntryAsync(
            TranscriptKinds.UserPrompt, body[..PromptSubmissionMatch.MatchWindowChars], timestamp: harness.Now);

        harness.Adapter.DialogAdopted = false;
        harness.Adapter.RenderedScreenOverride = null;
        RecordRealPrompts(harness);
        await harness.Queue.FlushSessionAsync(harness.SessionId, ct);

        var row = await ReloadAsync(harness, id, ct);
        row.Status.ShouldBe(QueuedMessageStatus.Sent);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        row.DeliveryAttempts.ShouldBe(1);
        row.DeliveryVerdict.ShouldNotBe(DeliveryVerdict.Truncated);
        harness.Adapter.SubmittedBodies.ShouldBe([body]);
        await using var incidents = harness.CreateObserverContext();
        (await incidents.AgentIncidents.CountAsync(i => i.AgentId == harness.AgentId
            && i.Kind == AgentIncidentKind.TruncatedTerminalDelivery, ct)).ShouldBe(0);
    }

    [Test]
    [Timeout(180_000)]
    public async Task C1591_ClearWithholdTypesEachNoteOnce(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var harness = await ClaudeDialogFixture.CreateBridgeAsync(
            new BridgeQueueHarness.HarnessOptions { ConnectionString = schema.ConnectionString });
        await harness.InsertStoreVisibleTurnAsync("earlier operator prompt", "earlier reply");
        string[] bodies = ["first withheld notice alpha", "second withheld notice beta", "third withheld notice gamma"];
        harness.Adapter.DialogAdopted = true;
        foreach (var body in bodies)
            await harness.Queue.EnqueueAsync(harness.SessionId, body, MessageSendMode.WhenIdle, ct);
        harness.Adapter.Inputs.ShouldBeEmpty();
        harness.Adapter.SubmittedBodies.ShouldBeEmpty();

        harness.Adapter.DialogAdopted = false;
        harness.Adapter.RenderedScreenOverride = null;
        RecordRealPrompts(harness);
        for (var n = 0; n < bodies.Length; n++)
            await harness.Queue.FlushSessionAsync(harness.SessionId, ct);

        harness.Adapter.SubmittedBodies.ShouldBe(bodies);
        await using var verify = harness.CreateObserverContext();
        var rows = await verify.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == harness.SessionId && bodies.Contains(m.Body))
            .OrderBy(m => m.Sequence)
            .ToListAsync(ct);
        rows.Count.ShouldBe(bodies.Length);
        for (var n = 0; n < bodies.Length; n++)
        {
            rows[n].Body.ShouldBe(bodies[n]);
            rows[n].Status.ShouldBe(QueuedMessageStatus.Sent);
            rows[n].DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
            rows[n].DeliveryAttempts.ShouldBe(1);
        }

        var prompts = await verify.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == harness.SessionId && t.Kind == TranscriptKinds.UserPrompt
                && bodies.Contains(t.Text))
            .OrderBy(t => t.Sequence)
            .Select(t => t.Text)
            .ToListAsync(ct);
        prompts.ShouldBe(bodies);
    }

    [Test]
    [Timeout(180_000)]
    public async Task C1591_OneAttemptCapDoesNotParkAWithhold(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var harness = await ClaudeDialogFixture.CreateBridgeAsync(
            new BridgeQueueHarness.HarnessOptions
            {
                ConnectionString = schema.ConnectionString,
                ConfigureDeliveryVerification = v => v.MaxDeliveryAttempts = 1,
            });
        await harness.InsertStoreVisibleTurnAsync("earlier operator prompt", "earlier reply");
        harness.Adapter.DialogAdopted = true;
        var queued = await harness.Queue.EnqueueAsync(
            harness.SessionId, "withheld under a one-attempt cap", MessageSendMode.WhenIdle, ct);
        var message = queued.Messages.Single(m => m.Body == "withheld under a one-attempt cap");
        message.Parked.ShouldBeFalse();
        message.DeliveryAttempts.ShouldBe(0);
        var stored = await ReloadAsync(harness, message.Id, ct);
        stored.Status.ShouldBe(QueuedMessageStatus.Pending);
        stored.DeliveryAttempts.ShouldBe(0);
        stored.LastDeliveryStartedAt.ShouldBeNull();
        stored.LastDeliveryBaselineSequence.ShouldBeNull();
        await harness.Queue.OnTurnEndAsync(harness.SessionId, ct);
        await harness.Queue.SendNowAsync(harness.SessionId, message.Id, ct);
        stored = await ReloadAsync(harness, message.Id, ct);
        stored.DeliveryAttempts.ShouldBe(0);
        stored.Status.ShouldBe(QueuedMessageStatus.Pending);
        harness.Adapter.Inputs.ShouldBeEmpty();
        harness.Adapter.SubmittedBodies.ShouldBeEmpty();
    }

    private static void RecordRealPrompts(BridgeQueueHarness harness)
    {
        harness.Adapter.OnSubmitted = async body =>
        {
            await StoreAwareTranscript.WriteAsync(harness.Provider, harness.SessionId, async () =>
            {
                await harness.InsertTranscriptEntryAsync(
                    TranscriptKinds.UserPrompt, body, timestamp: DateTime.UtcNow);
                await harness.InsertTranscriptEntryAsync(
                    TranscriptKinds.TurnEnd, stopReason: "end_turn", timestamp: DateTime.UtcNow);
            });
        };
    }

    private static async Task<QueuedMessageDto> ExpectWithheldAsync(
        BridgeQueueHarness harness, string body, string reason, int attempts, CancellationToken ct)
    {
        var inputs = harness.Adapter.Inputs.Count;
        var queue = await harness.Queue.EnqueueAsync(harness.SessionId, body, MessageSendMode.WhenIdle, ct);
        harness.Adapter.Inputs.Count.ShouldBe(inputs);
        harness.Adapter.SubmittedBodies.ShouldBeEmpty();
        queue.ModalBlocked.ShouldBeTrue();
        queue.ModalBlockedReason.ShouldBe(reason);
        var message = queue.Messages.Single(m => m.Body == body);
        message.Status.ShouldBe(QueuedMessageStatus.Pending.ToString());
        message.DeliveryAttempts.ShouldBe(attempts);
        message.WithholdReason.ShouldBe(reason);
        var stored = await ReloadAsync(harness, message.Id, ct);
        stored.Status.ShouldBe(QueuedMessageStatus.Pending);
        stored.DeliveryAttempts.ShouldBe(attempts);
        stored.LastDeliveryGeneration.ShouldBeNull();
        if (attempts == 0)
        {
            stored.LastDeliveryStartedAt.ShouldBeNull();
            stored.LastDeliveryBaselineSequence.ShouldBeNull();
        }
        return message;
    }

    private static async Task AssertFloorAsync(
        BridgeQueueHarness harness, Guid messageId, long floor, DateTime generation, CancellationToken ct)
    {
        await using var db = harness.CreateObserverContext();
        var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == messageId, ct);
        row.LastDeliveryBaselineSequence.ShouldBe(floor);
        SessionGeneration.Equal(
            row.LastDeliveryStartedAt,
            generation.AddTicks(-SessionGeneration.MicrosecondTicks)).ShouldBeTrue();
        var prompts = LandNoteReceipt.Prompts(
            db.TranscriptEntries, harness.SessionId, false, LandNotificationKind.Outcome,
            row.LastDeliveryBaselineSequence, row.LastDeliveryStartedAt, 30);
        prompts.ShouldNotBeNull();
        (await prompts.CountAsync(ct)).ShouldBe(0);
    }

    private static async Task<SessionQueuedMessage> ReloadAsync(
        BridgeQueueHarness harness, Guid messageId, CancellationToken ct)
    {
        await using var db = harness.CreateObserverContext();
        return await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == messageId, ct);
    }
}
