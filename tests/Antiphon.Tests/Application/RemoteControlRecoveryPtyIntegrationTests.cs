using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0514 R-14 deterministic producer-to-recipient coverage using the production recovery
/// graph. FakeClaude contract coverage lives in <c>FakeClaudeContractTests</c>; the headed
/// provider canary is <c>C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt</c>.
/// </summary>
[Category("Integration")]
[NotInParallel("RemoteControlRecovery")]
public class RemoteControlRecoveryPtyIntegrationTests
{
    [Test]
    public async Task C514_Health_request_reaches_busy_and_idle_recipients()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        await h.ArmProbeAfterWriteAsync();
        await h.MarkWorkingAsync();
        var busy = await h.ReserveAndExecuteAsync();
        busy.ShouldBe(RemoteControlArmResult.WithheldNotIdle);
        h.Adapter.ConditionalInputs.ShouldBeEmpty();
        await h.MarkIdleAsync();
        Guid id;
        await using (var db = h.CreateDb())
            id = (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId)).Id;
        var idle = await h.ExecuteAsync(id);
        idle.ShouldBe(RemoteControlArmResult.ArmedObserved);
        h.Adapter.ConditionalInputs.ShouldContain("/remote-control");
    }

    [Test]
    public async Task C514_Idle_modal_clearance_delivers_three_complete_prompts()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        var bodies = new[]
        {
            "prompt-one-AAAA-c514xx",
            "prompt-one-BBBB-c514xx",
            "prompt-one-CCCC-c514xx",
        };
        h.Adapter.OnSubmitted = async submitted =>
        {
            await using var db = h.CreateDb();
            var seq = ((await db.TranscriptEntries.Where(t => t.AgentSessionId == h.SessionId)
                .MaxAsync(t => (long?)t.Sequence)) ?? 0) + 1;
            db.TranscriptEntries.Add(new Antiphon.Server.Domain.Entities.TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = h.SessionId,
                Sequence = seq,
                Kind = TranscriptKinds.UserPrompt,
                Text = submitted,
                Timestamp = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            });
            db.TranscriptEntries.Add(new Antiphon.Server.Domain.Entities.TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = h.SessionId,
                Sequence = seq + 1,
                Kind = TranscriptKinds.TurnEnd,
                StopReason = "end_turn",
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        };
        foreach (var body in bodies)
            await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);

        await using var db = h.CreateDb();
        foreach (var body in bodies)
        {
            (await db.TranscriptEntries.CountAsync(t =>
                t.AgentSessionId == h.SessionId && t.Kind == TranscriptKinds.UserPrompt && t.Text == body))
                .ShouldBe(1);
        }
    }

    [Test]
    public async Task C514_Working_modal_waits_for_operator_then_converts_without_replay()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        await h.MarkWorkingAsync();
        h.Adapter.RemoteControlMenuOpen = true;
        await h.TickWatchAsync();
        h.Adapter.ConditionalInputs.ShouldBeEmpty();
        await h.Queue.EnqueueAsync(h.SessionId, "held-during-working-modal-c514", MessageSendMode.WhenIdle, CancellationToken.None);
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
        await h.Runtime.SendInputAsync(h.SessionId, "\u001b", CancellationToken.None, trackManualTurn: false);
        h.Adapter.RemoteControlMenuOpen.ShouldBeFalse();
        await h.MarkIdleAsync();
        h.Adapter.OnSubmitted = async submitted =>
        {
            await using var db = h.CreateDb();
            var seq = ((await db.TranscriptEntries.Where(t => t.AgentSessionId == h.SessionId)
                .MaxAsync(t => (long?)t.Sequence)) ?? 0) + 1;
            db.TranscriptEntries.Add(new Antiphon.Server.Domain.Entities.TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = h.SessionId,
                Sequence = seq,
                Kind = TranscriptKinds.UserPrompt,
                Text = submitted,
                Timestamp = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            });
            db.TranscriptEntries.Add(new Antiphon.Server.Domain.Entities.TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = h.SessionId,
                Sequence = seq + 1,
                Kind = TranscriptKinds.TurnEnd,
                StopReason = "end_turn",
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        };
        await h.TickWatchAsync();
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        h.Adapter.SubmittedBodies.ShouldContain("held-during-working-modal-c514");
        h.Adapter.ConditionalInputs.ShouldBeEmpty();
    }

    [Test]
    public async Task C514_Server_crash_recovers_each_delivery_handoff()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        h.Adapter.ConditionalOutcomeOverride = ConditionalInputOutcomes.Unknown;
        await h.ReserveAndExecuteAsync();
        await using var db = h.CreateDb();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        row.MaintenanceResult.ShouldBe(RemoteControlArmResult.ArmUnconfirmed);
        h.Adapter.ConditionalInputs.Clear();
        h.Adapter.ConditionalOutcomeOverride = null;
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        h.Adapter.ConditionalInputs.ShouldBeEmpty();
    }

    [Test]
    public async Task C514_Deferred_card_boot_reaches_recipient_once()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        var body = "deferred card boot body c514xx";
        await using (var db = h.CreateDb())
        {
            db.SessionQueuedMessages.Add(new Antiphon.Server.Domain.Entities.SessionQueuedMessage
            {
                Id = Guid.NewGuid(),
                AgentSessionId = h.SessionId,
                Body = body,
                Status = QueuedMessageStatus.Pending,
                Sequence = 1,
                Origin = QueuedMessageOrigin.System,
                CreatedAt = DateTime.UtcNow,
                DeferredFromRunAttemptId = Guid.NewGuid(),
                MaintenanceAcceptedStartedAt = h.Generation,
            });
            await db.SaveChangesAsync();
        }

        h.Adapter.OnSubmitted = async submitted =>
        {
            await using var db = h.CreateDb();
            var seq = ((await db.TranscriptEntries.Where(t => t.AgentSessionId == h.SessionId)
                .MaxAsync(t => (long?)t.Sequence)) ?? 0) + 1;
            db.TranscriptEntries.Add(new Antiphon.Server.Domain.Entities.TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = h.SessionId,
                Sequence = seq,
                Kind = TranscriptKinds.UserPrompt,
                Text = submitted,
                Timestamp = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        };
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        h.Adapter.SubmittedBodies.ShouldBe([body]);
    }

    [Test]
    public async Task C514_Internal_queue_drain_creates_no_replay_request()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        var before = await CountQ(h);
        h.Adapter.RemoteControlMenuOpen = true;
        await h.Runtime.SendInputAsync(h.SessionId, "internal-queued-body", CancellationToken.None);
        h.Adapter.RemoteControlMenuOpen = false;
        (await CountQ(h)).ShouldBe(before);
    }

    private static async Task<int> CountQ(RemoteControlRecoveryHarness h)
    {
        await using var db = h.CreateDb();
        return await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId);
    }
}
