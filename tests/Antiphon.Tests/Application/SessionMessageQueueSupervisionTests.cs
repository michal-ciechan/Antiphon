using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0082 S3 — the two Supervision-origin queue arms: cancel-not-strand (a /compact that
/// cannot deliver immediately is dropped, not left for the next turn-end) and cancel-not-park
/// (spent attempts cancel with a Warning incident rather than parking for a human).
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class SessionMessageQueueSupervisionTests
{
    private static AppDbContext CreateContext() => BridgeQueueHarness.CreateContext();

    [Test]
    public async Task A_supervision_enqueue_on_a_working_session_cancels_instead_of_stranding()
    {
        await using var h = await BridgeQueueHarness.CreateAsync();
        await h.MarkWorkingAsync();

        await h.Queue.EnqueueAsync(
            h.SessionId, ContextCompactionService.CompactTriggerBody,
            MessageSendMode.WhenIdle, CancellationToken.None, QueuedMessageOrigin.Supervision);

        await using var db = CreateContext();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        row.Origin.ShouldBe(QueuedMessageOrigin.Supervision);
        row.Status.ShouldBe(QueuedMessageStatus.Canceled);
        row.CanceledAt.ShouldNotBeNull();
        (await h.Queue.GetQueueAsync(h.SessionId, CancellationToken.None)).Messages.ShouldBeEmpty();
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
    }

    [Test]
    public async Task A_turn_end_cancels_a_pending_supervision_compact_instead_of_delivering_it()
    {
        await using var h = await BridgeQueueHarness.CreateAsync();
        await using (var db = CreateContext())
        {
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(),
                AgentSessionId = h.SessionId,
                Body = ContextCompactionService.CompactTriggerBody,
                Status = QueuedMessageStatus.Pending,
                Sequence = 1,
                Origin = QueuedMessageOrigin.Supervision,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var row = await verify.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        row.Status.ShouldBe(QueuedMessageStatus.Canceled);
        row.CanceledAt.ShouldNotBeNull();
        h.Adapter.SubmittedBodies.ShouldBeEmpty(
            "a compact that sat through a turn must not fire at the moment the session became active");
    }

    [Test]
    public async Task A_supervision_message_at_the_attempts_cap_cancels_instead_of_parking()
    {
        await using var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            Supervision = new SupervisionSettings
            {
                DeliveryVerification = new DeliveryVerificationSettings
                {
                    Enabled = true,
                    EvidenceTimeoutSeconds = 1,
                    PollIntervalMs = 50,
                    PostSubmitAdvanceTimeoutSeconds = 1,
                    StrandedAgeSeconds = 0,
                    TranscriptConfirmTimeoutSeconds = 2,
                    ReEnterIntervalSeconds = 1,
                    MaxDeliveryAttempts = 1,
                    PostFailureConfirmGraceSeconds = 1,
                },
            },
        });

        // Without a stored transcript the confirm gate degrades to screen-only, and a swallowed
        // Enter still redraws — so this would look Delivered. Seed a turn so the baseline exists.
        await h.InsertTurnAsync("an earlier prompt", "an earlier answer");
        h.Adapter.SwallowSubmits = 99;

        await h.Queue.EnqueueAsync(
            h.SessionId, ContextCompactionService.CompactTriggerBody,
            MessageSendMode.WhenIdle, CancellationToken.None, QueuedMessageOrigin.Supervision);

        h.Adapter.Killed.ShouldBeFalse("never kill a session over a dropped auto-compact");

        await using var db = CreateContext();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        row.Origin.ShouldBe(QueuedMessageOrigin.Supervision);
        row.Status.ShouldBe(QueuedMessageStatus.Canceled, "cancel-not-park");
        row.CanceledAt.ShouldNotBeNull();
        row.DeliveryAttempts.ShouldBeGreaterThanOrEqualTo(1);

        var incident = await db.AgentIncidents.SingleAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.AutoCompactFailed);
        incident.Severity.ShouldBe(AlertSeverity.Warning);
        incident.FailureReason.ShouldBe("DeliveryFailed");
        incident.Message.ShouldContain("canceled rather than parked");

        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed))
            .ShouldBeFalse("the park arm is replaced, not stacked on the ordinary delivery incident");
    }
}
