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
