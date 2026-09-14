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
    /// A busy recipient is withheld with ZERO bytes at the child; an idle one arms, and the only
    /// thing accepted as proof of the arm is what the CHILD ITSELF printed.
    /// </summary>
    [Test]
    public async Task C514_Health_request_reaches_busy_and_idle_recipients()
    {
        await using var lane = await RemoteControlPtyLane.CreateAsync(rcScenario: "c514");

        // One real round trip first, so the child's own JSONL is the transcript everything below
        // reasons about.
        const string work = "c514 lane work body before the arm";
        await lane.Queue.EnqueueAsync(lane.SessionId, work, MessageSendMode.Now, CancellationToken.None);
        (await lane.WaitForPersistedPromptsAsync([work], TimeSpan.FromSeconds(20)))
            .ShouldBeTrue("the child must record its prompt in its own JSONL");

        await lane.MarkWorkingAsync("c514 lane body the recipient is still working on");
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
        idle.ShouldBe(RemoteControlArmResult.ArmedObserved, "screen tail: " + Tail(lane));

        // The child's own output is the receipt: fakeclaude only prints these after a SUBMITTED
        // /remote-control actually reached it through the guarded conditional write.
        lane.RawOutput().ShouldContain("SUBMITTED:/remote-control");
        lane.RawOutput().ShouldContain("remote-control is active");
        lane.RawOutput().ShouldContain("RCMENU:armed");

        await using (var db = lane.CreateDb())
        {
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
            row.MaintenanceResult.ShouldBe(RemoteControlArmResult.ArmedObserved);
            row.MaintenanceAcceptedStartedAt.ShouldNotBeNull();
            row.SubmissionStartedAt.ShouldNotBeNull();
        }

        // An arm is not a work delivery: the child's own file holds exactly the one real body and
        // no command prompt, and nothing invented a UserPrompt receipt for /remote-control.
        lane.ChildFileUserPrompts().ShouldBe([work]);
        (await lane.PersistedUserPromptsAsync()).ShouldNotContain("/remote-control");
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
        // No conditional transport: the modal is detected and surfaced but no automatic Esc is
        // possible (D-8), so every hold below is the modal barrier and nothing else.
        await using var h = await RemoteControlRecoveryHarness.CreateAsync(advertiseConditional: false);
        string[] bodies =
        [
            "c514 reconcile shared prefix alpha",
            "c514 reconcile shared prefix bravo",
            "c514 reconcile shared prefix charlie",
        ];

        // The first body is an INTERRUPTED attempt: typed once, its receipt never committed. That
        // is the only shape a late prompt may confirm — a never-attempted Q must never adopt a
        // stray matching record as its own receipt.
        await h.MarkIdleAsync();
        await using (var seed = h.CreateDb())
        {
            var baseline = await seed.TranscriptEntries
                .Where(t => t.AgentSessionId == h.SessionId)
                .MaxAsync(t => (long?)t.Sequence) ?? 0;
            seed.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(),
                AgentSessionId = h.SessionId,
                Body = bodies[0],
                Status = QueuedMessageStatus.Pending,
                Sequence = 1,
                Origin = QueuedMessageOrigin.Ui,
                CreatedAt = DateTime.UtcNow.AddMinutes(-1),
                DeliveryAttempts = 1,
                LastDeliveryStartedAt = DateTime.UtcNow.AddMinutes(-1),
                LastDeliveryBaselineSequence = baseline,
            });
            await seed.SaveChangesAsync();
        }

        // No conditional transport means no automatic Esc is even possible, so every hold below is
        // the modal barrier on an IDLE session — not the working gate, and not a dismissal race.
        h.Adapter.RemoteControlMenuOpen = true;
        await h.TickWatchAsync();
        Escs(h).ShouldBe(0, "an unsupported transport withholds the automatic Esc");

        foreach (var body in bodies.Skip(1))
            await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);
        h.Adapter.SubmittedBodies.ShouldBeEmpty("an open modal withholds every body");

        // The child's own record for the interrupted attempt lands late, above its baseline.
        await h.Inner.InsertTranscriptEntryAsync(
            TranscriptKinds.UserPrompt, bodies[0], timestamp: DateTime.UtcNow);
        // A truncated paint of the second is not that body.
        await h.Inner.InsertTranscriptEntryAsync(
            TranscriptKinds.UserPrompt, bodies[1][..10], timestamp: DateTime.UtcNow);
        await h.MarkIdleAsync();

        // External clearance: the operator closed it from their own terminal.
        h.Adapter.OnSubmitted = Record(h);
        h.Adapter.RemoteControlMenuOpen = false;
        await h.TickWatchAsync();
        Escs(h).ShouldBe(0, "an externally cleared modal earns no automatic Esc");
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        h.Adapter.SubmittedBodies.ShouldNotContain(bodies[0],
            "a complete matching prompt above the baseline confirms that Q without replaying it");
        h.Adapter.SubmittedBodies.Count(b => b == bodies[1])
            .ShouldBe(1, "a truncated prompt confirms nothing, so the body is typed once");
        h.Adapter.SubmittedBodies.Count(b => b == bodies[2])
            .ShouldBe(1, "an unattempted body has no receipt to reconcile, so it is typed once");

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
        await using var h = await RemoteControlRecoveryHarness.CreateAsync(advertiseConditional: false);
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

        // Idle, but the child ignores Esc: the episode stays open and only the barrier can be
        // holding these two rows back.
        await h.MarkIdleAsync();
        h.Adapter.RemoteControlMenuOpen = true;
        await h.TickWatchAsync();
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        h.Adapter.SubmittedBodies.ShouldBeEmpty("a known-open modal defers both handoffs");

        h.Adapter.OnSubmitted = Record(h);
        h.Adapter.RemoteControlMenuOpen = false;
        await h.TickWatchAsync();

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

    /// <summary>A submitted body becomes the child's own prompt record and ends its turn.</summary>
    private static Func<string, Task> Record(RemoteControlRecoveryHarness h) => async submitted =>
    {
        await h.Inner.InsertTranscriptEntryAsync(
            TranscriptKinds.UserPrompt, submitted, timestamp: DateTime.UtcNow);
        await h.Inner.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
    };

    private static string Tail(RemoteControlPtyLane lane)
    {
        var raw = lane.RawOutput();
        return raw.Length <= 1200 ? raw : raw[^1200..];
    }

    /// <summary>Automatic Esc writes attempted so far, across every episode on this session.</summary>
    private static int Escs(RemoteControlRecoveryHarness h) =>
        h.Adapter.ConditionalInputs.Count(i => i == RemoteControlRecoveryService.EscPayload);

    private static async Task<int> CountQ(RemoteControlRecoveryHarness h)
    {
        await using var db = h.CreateDb();
        return await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId);
    }
}
