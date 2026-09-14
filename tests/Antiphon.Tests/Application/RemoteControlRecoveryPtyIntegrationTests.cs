using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
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
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public class RemoteControlRecoveryPtyIntegrationTests
{
    /// <summary>
    /// V-8 / R-14, real lane: producer -> queue -> runtime -> runner -> real ConPTY -> real child.
    /// A busy recipient is withheld with ZERO bytes at the child, and the idle one arms only when
    /// the CHILD ITSELF prints its arm marker. The busy state is the child's own (it took a prompt
    /// under ANTIPHON_FAKE_NO_REPLY and never answered), not a row this test wrote.
    /// </summary>
    [Test]
    public async Task C514_Health_request_reaches_busy_and_idle_recipients()
    {
        await using var lane = await RemoteControlPtyLane.CreateAsync(
            rcScenario: "c514",
            extraEnv: new Dictionary<string, string> { ["ANTIPHON_FAKE_NO_REPLY"] = "1" });

        // Make the recipient genuinely busy: a real submitted turn the child records and never ends.
        const string work = "c514 lane work body that hangs";
        await lane.Queue.EnqueueAsync(lane.SessionId, work, MessageSendMode.Now, CancellationToken.None);
        (await lane.WaitForPersistedPromptsAsync([work], TimeSpan.FromSeconds(20)))
            .ShouldBeTrue("the child must record the hung turn's prompt in its own JSONL");

        var busy = await lane.ReserveAndExecuteAsync();
        busy.ShouldBe(RemoteControlArmResult.WithheldNotIdle);
        lane.RawOutput().ShouldNotContain("SUBMITTED:/remote-control");
        lane.RawOutput().ShouldNotContain("RCMENU:armed");

        Guid id;
        await using (var db = lane.CreateDb())
            id = (await db.SessionQueuedMessages
                .SingleAsync(m => m.AgentSessionId == lane.SessionId && m.MaintenanceSlotActive)).Id;

        await lane.MarkIdleAsync();
        var idle = await lane.ExecuteAsync(id);

        // The child's own output is the receipt: fakeclaude only prints these after a SUBMITTED
        // /remote-control actually reached it through the guarded conditional write.
        lane.RawOutput().ShouldContain("SUBMITTED:/remote-control");
        lane.RawOutput().ShouldContain("remote-control is active");
        lane.RawOutput().ShouldContain("RCMENU:armed");
        idle.ShouldBe(RemoteControlArmResult.ArmedObserved);

        await using (var db = lane.CreateDb())
        {
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
            row.MaintenanceResult.ShouldBe(RemoteControlArmResult.ArmedObserved);
            row.MaintenanceAcceptedStartedAt.ShouldNotBeNull();
            row.SubmissionStartedAt.ShouldNotBeNull();
        }

        // An arm is not a work delivery: no UserPrompt for the command body, in the child's file
        // or in the ingested copy. The hung turn's body is the only prompt either side ever saw.
        lane.ChildFileUserPrompts().ShouldBe([work]);
        (await lane.PersistedUserPromptsAsync()).ShouldBe([work]);
    }

    /// <summary>
    /// V-8 / R-14, real lane: after a REAL management menu is cleared by one Esc, three queued
    /// bodies with equal prefixes and distinct tails each arrive exactly once as a complete
    /// UserPrompt in the CHILD'S OWN JSONL, and the ingested copy matches it record for record.
    /// </summary>
    [Test]
    public async Task C514_Idle_modal_clearance_delivers_three_complete_prompts()
    {
        await using var lane = await RemoteControlPtyLane.CreateAsync(
            extraEnv: new Dictionary<string, string> { ["ANTIPHON_FAKE_RC_MENU"] = "1" });

        // A real modal, opened by a real local command travelling the production queue.
        await lane.Queue.EnqueueAsync(
            lane.SessionId, "/remote-control", MessageSendMode.Now, CancellationToken.None);
        (await lane.WaitForRawAsync(s => s.Contains("RCMENU:open"), TimeSpan.FromSeconds(20)))
            .ShouldBeTrue("the child must open its management menu. Screen:\n" + lane.RawOutput());

        await lane.Runtime.SendInputAsync(
            lane.SessionId, RemoteControlRecoveryService.EscPayload, CancellationToken.None,
            trackManualTurn: false);
        (await lane.WaitForRawAsync(s => s.Contains("RCMENU:closed"), TimeSpan.FromSeconds(20)))
            .ShouldBeTrue("one Esc must clear the menu. Screen:\n" + lane.RawOutput());

        string[] bodies =
        [
            "c514 lane shared prefix alpha",
            "c514 lane shared prefix bravo",
            "c514 lane shared prefix charlie",
        ];
        foreach (var body in bodies)
            await lane.Queue.EnqueueAsync(lane.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);
        await lane.Queue.FlushSessionAsync(lane.SessionId, CancellationToken.None);

        (await lane.WaitForPersistedPromptsAsync(bodies, TimeSpan.FromSeconds(30)))
            .ShouldBeTrue("every queued body must reach the child. Screen:\n" + lane.RawOutput());

        // The child wrote these; nothing in this test or its harness inserts a UserPrompt row.
        var fromChildFile = lane.ChildFileUserPrompts();
        fromChildFile.ShouldBe(bodies);
        (await lane.PersistedUserPromptsAsync()).ShouldBe(bodies);
        foreach (var body in bodies)
            fromChildFile.Count(p => p == body).ShouldBe(1, $"'{body}' must arrive exactly once");
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


    /// <summary>
    /// CARD-0514 R-10: after an EXTERNAL clear (no automatic Esc is attributable), every held
    /// message's identity is reconciled before anything is retyped. A late but complete matching
    /// prompt confirms its own Q and nothing else; a truncated prompt and a pre-baseline prompt
    /// confirm nothing, so their bodies are still typed — exactly once each.
    /// </summary>
    [Test]
    public async Task C514_Clearance_reconciles_each_message_identity_before_redelivery()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        string[] bodies =
        [
            "c514 reconcile shared prefix alpha",
            "c514 reconcile shared prefix bravo",
            "c514 reconcile shared prefix charlie",
        ];

        // An OLD prompt for the third body, before its Q even exists: it is below that Q's
        // baseline, so it can never be that Q's receipt.
        await h.Inner.InsertTranscriptEntryAsync(
            TranscriptKinds.UserPrompt, bodies[2], timestamp: DateTime.UtcNow);
        await h.MarkIdleAsync();

        h.Adapter.RemoteControlMenuOpen = true;
        await h.TickWatchAsync();
        foreach (var body in bodies)
            await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);
        h.Adapter.SubmittedBodies.ShouldBeEmpty("an open modal withholds every body");

        // The child had already taken the first body before the menu went up; its record lands late.
        await h.Inner.InsertTranscriptEntryAsync(
            TranscriptKinds.UserPrompt, bodies[0], timestamp: DateTime.UtcNow);
        // A truncated paint of the second is not that body.
        await h.Inner.InsertTranscriptEntryAsync(
            TranscriptKinds.UserPrompt, bodies[1][..10], timestamp: DateTime.UtcNow);
        await h.MarkIdleAsync();

        // External clearance: the operator closed it. Nothing here sends an Esc.
        h.Adapter.RemoteControlMenuOpen = false;
        await h.TickWatchAsync();
        h.Adapter.ConditionalInputs.ShouldBeEmpty("an externally cleared modal earns no automatic Esc");

        h.Adapter.OnSubmitted = submitted => h.Inner.InsertTranscriptEntryAsync(
            TranscriptKinds.UserPrompt, submitted, timestamp: DateTime.UtcNow);
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        h.Adapter.SubmittedBodies.ShouldNotContain(bodies[0],
            "a complete matching prompt above the baseline confirms that Q without retyping it");
        h.Adapter.SubmittedBodies.Count(b => b == bodies[1])
            .ShouldBe(1, "a truncated prompt confirms nothing, so the body is typed once");
        h.Adapter.SubmittedBodies.Count(b => b == bodies[2])
            .ShouldBe(1, "a pre-baseline prompt confirms nothing, so the body is typed once");

        await using var db = h.CreateDb();
        var rows = await db.SessionQueuedMessages
            .Where(m => m.AgentSessionId == h.SessionId)
            .ToListAsync();
        rows.Select(r => r.Body).OrderBy(b => b, StringComparer.Ordinal)
            .ShouldBe(bodies.OrderBy(b => b, StringComparer.Ordinal));
        rows.ShouldAllBe(r => r.Status == QueuedMessageStatus.Sent);
    }

    /// <summary>
    /// CARD-0514 R-11: a card boot that deferred its original work has more than one handoff to
    /// recover — the work prompt itself and the interactive note already routed through the queue.
    /// Both survive the barrier, both come back exactly once and in order, and two concurrent
    /// resumes produce one prompt each rather than a replay. The narrower single-handoff case is
    /// <see cref="C514_Deferred_card_boot_reaches_recipient_once"/>.
    /// </summary>
    [Test]
    public async Task C514_Deferred_card_work_recovers_all_handoffs()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        const string work = "c514 deferred card original work body";
        const string note = "c514 deferred interactive launch note body";
        var attempt = Guid.NewGuid();
        var generation = h.Generation;

        await using (var db = h.CreateDb())
        {
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(),
                AgentSessionId = h.SessionId,
                Body = work,
                Status = QueuedMessageStatus.Pending,
                Sequence = 1,
                Origin = QueuedMessageOrigin.System,
                CreatedAt = DateTime.UtcNow,
                DeferredFromRunAttemptId = attempt,
                MaintenanceAcceptedStartedAt = generation,
            });
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(),
                AgentSessionId = h.SessionId,
                Body = note,
                Status = QueuedMessageStatus.Pending,
                Sequence = 2,
                Origin = QueuedMessageOrigin.Ui,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        h.Adapter.RemoteControlMenuOpen = true;
        await h.TickWatchAsync();
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        h.Adapter.SubmittedBodies.ShouldBeEmpty("a known-open modal defers both handoffs");

        h.Adapter.RemoteControlMenuOpen = false;
        await h.TickWatchAsync();
        h.Adapter.OnSubmitted = submitted => h.Inner.InsertTranscriptEntryAsync(
            TranscriptKinds.UserPrompt, submitted, timestamp: DateTime.UtcNow);

        // Two resumes race for the same session: the per-session lock must make that one drain.
        await Task.WhenAll(
            h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None),
            h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None));

        h.Adapter.SubmittedBodies.Count(b => b == work).ShouldBe(1);
        h.Adapter.SubmittedBodies.Count(b => b == note).ShouldBe(1);
        h.Adapter.SubmittedBodies.ShouldBe([work, note], "sequence order survives the barrier");

        await using (var db = h.CreateDb())
        {
            var rows = await db.SessionQueuedMessages
                .Where(m => m.AgentSessionId == h.SessionId)
                .OrderBy(m => m.Sequence)
                .ToListAsync();
            rows.Count.ShouldBe(2, "recovery must not invent a new queue identity for either handoff");
            rows[0].DeferredFromRunAttemptId.ShouldBe(attempt, "the work prompt keeps its RunAttempt identity");
            rows.ShouldAllBe(r => r.Status == QueuedMessageStatus.Sent);
        }
    }

    private static async Task<int> CountQ(RemoteControlRecoveryHarness h)
    {
        await using var db = h.CreateDb();
        return await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId);
    }
}
