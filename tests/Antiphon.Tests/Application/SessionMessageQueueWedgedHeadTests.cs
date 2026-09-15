using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
[Category("Slow")]
public class SessionMessageQueueWedgedHeadTests
{
    private const string Body = "[antiphon-task:419b8b34] role=Check tier=Low workspace=Shared";
    private static AppDbContext CreateContext() => BridgeQueueHarness.CreateContext();
    private static Task<BridgeQueueHarness> CreateAsync(
        bool alwaysOn = false, Action<IServiceCollection>? configureServices = null) =>
        BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = alwaysOn,
            // These cases judge the recovery verdict, not delayed transcript ingestion.
            ConfigureDeliveryVerification = v => v.PostFailureConfirmGraceSeconds = 0,
            ConfigureServices = configureServices,
        });

    private static async Task<DateTime> GenerationAsync(BridgeQueueHarness h)
    {
        await using var db = CreateContext();
        return SessionGeneration.Normalize(await db.AgentSessions.Where(s => s.Id == h.SessionId)
            .Select(s => s.StartedAt).SingleAsync());
    }

    private static async Task AdvanceGenerationAsync(BridgeQueueHarness h)
    {
        var next = SessionGeneration.Next(await GenerationAsync(h), DateTime.UtcNow);
        await using var db = CreateContext();
        await db.AgentSessions.Where(s => s.Id == h.SessionId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.StartedAt, next));
    }

    // The UserPrompt rows the fake recorded, in order: the only evidence that says WHAT actually
    // went in, as opposed to which row the queue decided to mark Delivered.
    private static async Task AssertUserPromptsAsync(BridgeQueueHarness h, IReadOnlyList<string> expected)
    {
        await using var db = CreateContext();
        var prompts = await db.TranscriptEntries.AsNoTracking()
            .Where(e => e.AgentSessionId == h.SessionId && e.Kind == TranscriptKinds.UserPrompt)
            .OrderBy(e => e.Sequence)
            .Select(e => e.Text)
            .ToListAsync();
        prompts.ShouldBe(expected);
    }

    private static async Task<SessionQueuedMessage> MessageAsync(Guid id)
    {
        await using var db = CreateContext();
        return await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    [Test]
    [Arguments("Enqueue")]
    [Arguments("SendNow")]
    [Arguments("Immediate")]
    public async Task Typed_attempt_records_the_session_generation(string path)
    {
        await using var h = await CreateAsync();
        if (path == "SendNow")
        {
            var id = await h.SeedPendingMessageAsync(Body);
            await h.Queue.SendNowAsync(h.SessionId, id, CancellationToken.None);
        }
        else if (path == "Immediate")
            await h.Queue.EnqueueDeliveringNowAsync(h.SessionId, Body, CancellationToken.None);
        else
            await h.Queue.EnqueueAsync(h.SessionId, Body, MessageSendMode.WhenIdle, CancellationToken.None);

        await using var db = CreateContext();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        row.LastDeliveryGeneration.ShouldBe(await GenerationAsync(h));
        row.Status.ShouldBe(QueuedMessageStatus.Sent);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        h.Adapter.Inputs.ShouldBe([Body, "\r"]);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Backend_unreachable_refund_clears_the_generation(bool immediate)
    {
        await using var h = await CreateAsync();
        h.Adapter.ThrowOnSend = new ServiceUnavailableException("Herdr is unreachable.", HerdrProblemTypes.Unreachable);
        if (immediate)
            await Should.ThrowAsync<ConflictException>(() => h.Queue.EnqueueDeliveringNowAsync(h.SessionId, Body,
                CancellationToken.None));
        else
            await h.Queue.EnqueueAsync(h.SessionId, Body, MessageSendMode.WhenIdle, CancellationToken.None);

        await using var db = CreateContext();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        row.LastDeliveryGeneration.ShouldBeNull();
        row.LastDeliveryStartedAt.ShouldBeNull();
        row.DeliveryAttempts.ShouldBe(0);
        row.Status.ShouldBe(QueuedMessageStatus.Pending);
        h.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task Head_row_typed_into_a_previous_generation_is_retyped_not_Entered()
    {
        await using var h = await CreateAsync();
        var id = await h.SeedPendingMessageAsync(Body, deliveryAttempts: 1,
            deliveryVerdict: DeliveryVerdict.NoTranscriptRecord);
        await AdvanceGenerationAsync(h);
        // Use the EXACT body in history: even whole-head evidence cannot rescue a dead composer.
        h.Adapter.RenderedScreenOverride = Body + "\n> ";

        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        h.Adapter.Inputs.ShouldBe([Body, "\r"]);
        var row = await MessageAsync(id);
        row.DeliveryAttempts.ShouldBe(2);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        row.LastDeliveryGeneration.ShouldBe(await GenerationAsync(h));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Replayed_marker_of_another_task_does_not_earn_Enter_only(bool interrupted)
    {
        await using var h = await CreateAsync();
        var id = await h.SeedPendingMessageAsync(Body, deliveryAttempts: 1,
            status: interrupted ? QueuedMessageStatus.Sent : QueuedMessageStatus.Pending,
            deliveryVerdict: interrupted ? null : DeliveryVerdict.NoTranscriptRecord);
        h.Adapter.RenderedScreenOverride = "[antiphon-task:a42e10c4]\n[antiphon-report:a42e10c4 done]\n> ";

        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        h.Adapter.Inputs.ShouldBe([Body, "\r"]);
        (await MessageAsync(id)).DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
    }

    [Test]
    public async Task Held_body_in_the_same_generation_still_gets_Enter_only()
    {
        await using var h = await CreateAsync();
        var id = await h.SeedPendingMessageAsync(Body, deliveryAttempts: 1);
        h.Adapter.PrimeComposer(Body);

        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        h.Adapter.Inputs.ShouldBe(["\r"]);
        h.Adapter.SubmittedBodies.ShouldBe([Body]);
        (await MessageAsync(id)).DeliveryAttempts.ShouldBe(1);
        (await MessageAsync(id)).DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
    }

    [Test]
    public async Task Interrupted_Sent_row_from_a_previous_generation_reverts_then_retypes()
    {
        await using var h = await CreateAsync();
        var id = await h.SeedPendingMessageAsync(Body, deliveryAttempts: 1, status: QueuedMessageStatus.Sent);
        await AdvanceGenerationAsync(h);
        h.Adapter.RenderedScreenOverride = Body + "\n> ";

        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        h.Adapter.Inputs.ShouldBe([Body, "\r"]);
        var row = await MessageAsync(id);
        row.DeliveryAttempts.ShouldBe(2);
        row.Status.ShouldBe(QueuedMessageStatus.Sent);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Legacy_row_without_a_generation_uses_the_attempt_clock(bool generationChanged)
    {
        await using var h = await CreateAsync();
        var current = await GenerationAsync(h);
        var id = await h.SeedPendingMessageAsync(Body, deliveryAttempts: 1, legacyNullGeneration: true,
            lastDeliveryStartedAt: generationChanged ? current.AddMinutes(-1) : current.AddSeconds(1));
        if (generationChanged) h.Adapter.RenderedScreenOverride = Body + "\n> ";
        else h.Adapter.PrimeComposer(Body);

        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        h.Adapter.Inputs.ShouldBe(generationChanged ? [Body, "\r"] : ["\r"]);
        var row = await MessageAsync(id);
        row.DeliveryAttempts.ShouldBe(generationChanged ? 2 : 1);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
    }

    [Test]
    public async Task Failed_Enter_only_recovery_charges_an_attempt()
    {
        await using var h = await CreateAsync();
        var baseline = await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        var id = await h.SeedPendingMessageAsync(Body, deliveryAttempts: 1, baselineSequence: baseline);
        var before = await MessageAsync(id);
        h.Adapter.PrimeComposer(Body);
        h.Adapter.SwallowSubmits = 99;

        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        var row = await MessageAsync(id);
        row.DeliveryAttempts.ShouldBe(2);
        row.Status.ShouldBe(QueuedMessageStatus.Pending);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.NoTranscriptRecord);
        row.LastDeliveryStartedAt.ShouldBe(before.LastDeliveryStartedAt);
        row.LastDeliveryBaselineSequence.ShouldBe(before.LastDeliveryBaselineSequence);
        row.LastDeliveryGeneration.ShouldBe(before.LastDeliveryGeneration);
        h.Adapter.Inputs.ShouldNotBeEmpty();
        h.Adapter.Inputs.ShouldAllBe(input => input == "\r");
    }

    [Test]
    public async Task Third_failed_Enter_only_recovery_parks_and_unblocks_the_queue()
    {
        await using var h = await CreateAsync();
        var baseline = await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        var id = await h.SeedPendingMessageAsync(Body, deliveryAttempts: 2, baselineSequence: baseline);
        const string nextBody = "the next live queue message";
        var nextId = await h.SeedPendingMessageAsync(nextBody);
        h.Adapter.PrimeComposer(Body);
        h.Adapter.SwallowSubmits = 99;

        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        (await MessageAsync(id)).DeliveryAttempts.ShouldBe(3);
        (await MessageAsync(id)).DeliveryVerdict.ShouldBe(DeliveryVerdict.NoTranscriptRecord);
        (await h.Queue.GetQueueAsync(h.SessionId, CancellationToken.None)).Messages
            .Single(m => m.Id == id).Parked.ShouldBeTrue();
        h.Adapter.Inputs.ShouldNotContain(nextBody);
        // A relaunch is the one release that needs no screen evidence: a new process has a new
        // composer, so the parked body cannot still be standing in it.
        await AdvanceGenerationAsync(h);
        h.Adapter.PrimeComposer("");
        h.Adapter.SwallowSubmits = 0;
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        h.Adapter.Inputs.ShouldContain(nextBody);
        // The exact prompt, not a containment match: the parked body must not ride along.
        h.Adapter.SubmittedBodies.ShouldBe([nextBody]);
        await AssertUserPromptsAsync(h, [nextBody]);
        (await MessageAsync(nextId)).DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        (await MessageAsync(id)).DeliveryAttempts.ShouldBe(3);
    }

    // CARD-0501 review F1. Parking the head charges the third attempt and stops re-typing it, but
    // it does NOT empty the composer: the body is still standing there, unsubmitted. Typing the
    // next queue row on top of it submits BOTH as one UserPrompt and marks the next row Delivered
    // for a prompt it never owned. Nothing here clears the composer or advances the generation —
    // that is the whole point of the case the earlier regression hid.
    [Test]
    public async Task Parked_head_left_in_the_composer_holds_the_next_message()
    {
        await using var h = await CreateAsync();
        var baseline = await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        var id = await h.SeedPendingMessageAsync(Body, deliveryAttempts: 2, baselineSequence: baseline);
        const string nextBody = "the next live queue message";
        var nextId = await h.SeedPendingMessageAsync(nextBody);
        h.Adapter.PrimeComposer(Body);
        h.Adapter.SwallowSubmits = 99;

        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        (await MessageAsync(id)).DeliveryAttempts.ShouldBe(3);

        // The composer still holds the parked body and the session was never restarted.
        h.Adapter.SwallowSubmits = 0;
        var inputsBefore = h.Adapter.Inputs.Count;
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        h.Adapter.Inputs.Skip(inputsBefore).ShouldBeEmpty();
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
        await AssertUserPromptsAsync(h, []);
        var next = await MessageAsync(nextId);
        next.Status.ShouldBe(QueuedMessageStatus.Pending);
        next.DeliveryAttempts.ShouldBe(0);
        next.DeliveryVerdict.ShouldBeNull();
        // The hold is a defer, never a park or a kill.
        (await h.Queue.GetQueueAsync(h.SessionId, CancellationToken.None)).Messages
            .Single(m => m.Id == nextId).Parked.ShouldBeFalse();
        h.Adapter.Killed.ShouldBeFalse();
    }

    // The other release: no relaunch, but the composer is demonstrably clear again (a human
    // submitted or cleared the parked body). The next row then delivers its OWN body, exactly.
    [Test]
    public async Task Cleared_composer_releases_the_next_message_with_its_exact_body()
    {
        await using var h = await CreateAsync();
        var baseline = await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        var id = await h.SeedPendingMessageAsync(Body, deliveryAttempts: 2, baselineSequence: baseline);
        const string nextBody = "the next live queue message";
        var nextId = await h.SeedPendingMessageAsync(nextBody);
        h.Adapter.PrimeComposer(Body);
        h.Adapter.SwallowSubmits = 99;

        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        (await MessageAsync(id)).DeliveryAttempts.ShouldBe(3);

        h.Adapter.PrimeComposer("");
        h.Adapter.SwallowSubmits = 0;
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        h.Adapter.SubmittedBodies.ShouldBe([nextBody]);
        await AssertUserPromptsAsync(h, [nextBody]);
        (await MessageAsync(nextId)).DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        (await MessageAsync(id)).DeliveryAttempts.ShouldBe(3);
    }

    // A parked row whose typing predates the CURRENT generation cannot be in this composer, so it
    // must not hold anything — the no-regression twin of the two cases above.
    [Test]
    public async Task Parked_head_from_a_previous_generation_does_not_hold_the_next_message()
    {
        await using var h = await CreateAsync();
        var baseline = await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.SeedPendingMessageAsync(Body, deliveryAttempts: 3, baselineSequence: baseline,
            lastDeliveryGeneration: SessionGeneration.Normalize(DateTime.UtcNow.AddMinutes(-30)));
        const string nextBody = "the next live queue message";
        var nextId = await h.SeedPendingMessageAsync(nextBody);
        // The parked body is still painted on screen; only the generation says it is not ours.
        h.Adapter.PrimeComposer(Body);

        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        h.Adapter.Inputs.ShouldContain(nextBody);
        (await MessageAsync(nextId)).DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
    }

    [Test]
    public async Task Failed_Enter_only_recovery_kills_the_generation_it_pressed_Enter_into()
    {
        await using var h = await CreateAsync(alwaysOn: true);
        var generation = await GenerationAsync(h);
        var baseline = await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        var id = await h.SeedPendingMessageAsync(Body, deliveryAttempts: 1, baselineSequence: baseline);
        h.Adapter.PrimeComposer(Body);
        h.Adapter.SwallowSubmits = 99;

        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        (await MessageAsync(id)).DeliveryAttempts.ShouldBe(2);
        (await MessageAsync(id)).DeliveryVerdict.ShouldBe(DeliveryVerdict.NoTranscriptRecord);
        h.Adapter.Killed.ShouldBeTrue();
        h.Adapter.KillGenerationCalls.ShouldBe([generation]);
        h.Adapter.KillCount.ShouldBe(0);
        await using var db = CreateContext();
        var incident = await db.AgentIncidents.SingleAsync(i => i.AgentId == h.AgentId
            && i.Kind == AgentIncidentKind.DeliveryVerificationFailed);
        incident.Message.ShouldContain("after an Enter-only recovery");
    }

    [Test]
    public async Task Working_session_still_withholds_the_kill_after_a_failed_Enter_only()
    {
        await using var h = await CreateAsync(alwaysOn: true);
        var baseline = await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        var id = await h.SeedPendingMessageAsync(Body, deliveryAttempts: 1, baselineSequence: baseline);
        h.Adapter.PrimeComposer(Body);
        // Delivery starts idle, then activity arrives without an ingested UserPrompt.
        h.Adapter.OnSubmitted = _ => h.MarkWorkingAsync();

        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        (await MessageAsync(id)).DeliveryAttempts.ShouldBe(2);
        (await MessageAsync(id)).DeliveryVerdict.ShouldBe(DeliveryVerdict.NoTranscriptRecord);
        // The confirm loop spends all three allowed Enters before declaring the missing record.
        h.Adapter.Inputs.ShouldBe(["\r", "\r", "\r"]);
        h.Adapter.Killed.ShouldBeFalse();
        h.Adapter.KillGenerationCalls.ShouldBeEmpty();
    }

    // CARD-0501 review F2. The charge and the incident are two writes in two scopes, and the order
    // is load-bearing: HandleDeliveryFailureAsync reloads the rows and decides "parked" from the
    // count it reads, so the count must already be durable. A crash in between must therefore cost
    // the attempt - losing it would let an Enter-only cycle repeat without bound, which is the loop
    // this card exists to close.
    [Test]
    public async Task A_crash_between_the_charge_and_the_failure_handler_keeps_the_attempt_charged()
    {
        var boundary = new CrashAtChargeBoundary();
        await using var h = await CreateAsync(
            configureServices: services => services.AddSingleton<LandDeliveryBoundary>(boundary));
        var baseline = await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        var id = await h.SeedPendingMessageAsync(Body, deliveryAttempts: 1, baselineSequence: baseline);
        h.Adapter.PrimeComposer(Body);
        h.Adapter.SwallowSubmits = 99;

        await Should.ThrowAsync<IOException>(
            () => h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None));

        boundary.Calls.ShouldBe(1);
        var charged = await MessageAsync(id);
        charged.DeliveryAttempts.ShouldBe(2, "the charge is committed before the handler is called");
        charged.Status.ShouldBe(QueuedMessageStatus.Pending);
        charged.DeliveryVerdict.ShouldBeNull("the handler never ran, so it stamped nothing");
        h.Adapter.Killed.ShouldBeFalse();
        await using (var db = CreateContext())
            (await db.AgentIncidents.AnyAsync(i => i.AgentId == h.AgentId)).ShouldBeFalse();

        // The bound still holds across the crash: the third cycle parks instead of Entering again.
        await Should.ThrowAsync<IOException>(
            () => h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None));
        (await MessageAsync(id)).DeliveryAttempts.ShouldBe(3);

        var enters = h.Adapter.Inputs.Count;
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        boundary.Calls.ShouldBe(2, "a parked row is never Entered again");
        h.Adapter.Inputs.Count.ShouldBe(enters);
        (await h.Queue.GetQueueAsync(h.SessionId, CancellationToken.None)).Messages
            .Single(m => m.Id == id).Parked.ShouldBeTrue();
    }

    private sealed class CrashAtChargeBoundary : LandDeliveryBoundary
    {
        public int Calls;
        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary != "queue-enter-only-charged")
                return Task.CompletedTask;
            Calls++;
            return Task.FromException(new IOException("owned crash between the charge and the handler"));
        }
    }

    // A recovered run can be a BATCH (CARD-0342): one composed body, one Enter, several rows. The
    // charge is per row, so a failure must move every row of the run - charging only the head would
    // leave the tail able to re-enter the same cycle forever behind a parked head.
    [Test]
    public async Task Failed_Enter_only_recovery_charges_every_row_of_the_batch()
    {
        await using var h = await CreateAsync();
        var baseline = await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        var started = DateTime.UtcNow - TimeSpan.FromMinutes(4);
        const string first = "batch first body that is long enough";
        const string second = "batch second body that is long enough";
        var firstId = await h.SeedPendingMessageAsync(first, deliveryAttempts: 1,
            baselineSequence: baseline, origin: QueuedMessageOrigin.Delegation,
            status: QueuedMessageStatus.Sent, lastDeliveryStartedAt: started,
            conversationKey: "task:c501-batch");
        var secondId = await h.SeedPendingMessageAsync(second, deliveryAttempts: 1,
            baselineSequence: baseline, origin: QueuedMessageOrigin.Delegation,
            status: QueuedMessageStatus.Sent, lastDeliveryStartedAt: started,
            conversationKey: "task:c501-batch");
        h.Adapter.PrimeComposer(ChannelPromptFormat.FormatBatch([first], second));
        h.Adapter.SwallowSubmits = 99;

        await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);

        h.Adapter.Inputs.ShouldAllBe(input => input == "\r", "one composed body, Enter only");
        foreach (var id in new[] { firstId, secondId })
        {
            var row = await MessageAsync(id);
            row.DeliveryAttempts.ShouldBe(2);
            row.Status.ShouldBe(QueuedMessageStatus.Pending);
            row.DeliveryVerdict.ShouldBe(DeliveryVerdict.NoTranscriptRecord);
            row.LastDeliveryStartedAt.ShouldNotBeNull().ShouldBe(started, TimeSpan.FromSeconds(1));
            row.LastDeliveryBaselineSequence.ShouldBe(baseline);
        }
    }

    private static async Task<Guid> AttachTaskAsync(Guid messageId, AgentTaskStatus status,
        DateTime? deadline = null)
    {
        await using var db = CreateContext();
        var id = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id, RootTaskId = id, Title = "CARD-0501 fixture", Goal = "check fixture",
            Role = deadline is null ? AgentTaskRole.Check : AgentTaskRole.Distill,
            Status = status, CreatedAt = DateTime.UtcNow,
            CompletedAt = status == AgentTaskStatus.Dispatched ? null : DateTime.UtcNow,
            FailureReason = "preserve this task outcome",
        });
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == messageId);
        row.ExecutionTaskId = id;
        row.ExecutionDeadlineAt = deadline;
        await db.SaveChangesAsync();
        return id;
    }

    [Test]
    [Arguments(AgentTaskStatus.Failed)]
    [Arguments(AgentTaskStatus.Canceled)]
    [Arguments(AgentTaskStatus.Succeeded)]
    public async Task Untyped_brief_of_a_failed_task_is_canceled_at_flush(AgentTaskStatus status)
    {
        await using var h = await CreateAsync();
        var id = await h.SeedPendingMessageAsync(Body);
        var taskId = await AttachTaskAsync(id, status);
        await using var beforeDb = CreateContext();
        var before = await beforeDb.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);

        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        var row = await MessageAsync(id);
        row.Status.ShouldBe(QueuedMessageStatus.Canceled);
        row.CanceledAt.ShouldNotBeNull();
        h.Adapter.Inputs.ShouldBeEmpty();
        await using var db = CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(status);
        task.CompletedAt.ShouldBe(before.CompletedAt);
        task.FailureReason.ShouldBe(before.FailureReason);
        task.ConcurrencyToken.ShouldBe(before.ConcurrencyToken);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Typed_brief_of_a_failed_task_from_a_previous_generation_is_canceled(bool legacy)
    {
        await using var h = await CreateAsync();
        var id = await h.SeedPendingMessageAsync(Body, deliveryAttempts: 1, legacyNullGeneration: legacy);
        await AttachTaskAsync(id, AgentTaskStatus.Failed);
        await AdvanceGenerationAsync(h);

        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        (await MessageAsync(id)).Status.ShouldBe(QueuedMessageStatus.Canceled);
        h.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task Typed_brief_of_a_failed_task_in_this_generation_is_left_to_recovery()
    {
        await using var h = await CreateAsync();
        var id = await h.SeedPendingMessageAsync(Body, deliveryAttempts: 1);
        await AttachTaskAsync(id, AgentTaskStatus.Failed);
        h.Adapter.PrimeComposer(Body);

        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        (await MessageAsync(id)).Status.ShouldBe(QueuedMessageStatus.Sent);
        (await MessageAsync(id)).DeliveryAttempts.ShouldBe(1);
        h.Adapter.Inputs.ShouldBe(["\r"]);
        h.Adapter.SubmittedBodies.ShouldBe([Body]);
    }

    [Test]
    public async Task Brief_of_a_dispatched_task_is_untouched()
    {
        await using var h = await CreateAsync();
        var id = await h.SeedPendingMessageAsync(Body);
        var taskId = await AttachTaskAsync(id, AgentTaskStatus.Dispatched);

        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        (await MessageAsync(id)).Status.ShouldBe(QueuedMessageStatus.Sent);
        h.Adapter.Inputs.ShouldBe([Body, "\r"]);
        await using var db = CreateContext();
        (await db.AgentTasks.SingleAsync(t => t.Id == taskId)).Status.ShouldBe(AgentTaskStatus.Dispatched);
    }

    [Test]
    public async Task Canceled_orphans_do_not_block_the_next_deliverable_row()
    {
        await using var h = await CreateAsync();
        var head = await h.SeedPendingMessageAsync(Body, deliveryAttempts: 1);
        await AttachTaskAsync(head, AgentTaskStatus.Failed);
        await AdvanceGenerationAsync(h);
        var second = await h.SeedPendingMessageAsync("orphan two");
        await AttachTaskAsync(second, AgentTaskStatus.Canceled);
        var third = await h.SeedPendingMessageAsync("orphan three");
        await AttachTaskAsync(third, AgentTaskStatus.Failed);
        const string liveBody = "a live check brief";
        var live = await h.SeedPendingMessageAsync(liveBody);
        await AttachTaskAsync(live, AgentTaskStatus.Dispatched);

        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        foreach (var id in new[] { head, second, third })
            (await MessageAsync(id)).Status.ShouldBe(QueuedMessageStatus.Canceled);
        (await MessageAsync(live)).DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        h.Adapter.Inputs.ShouldBe([liveBody, "\r"]);
    }

    [Test]
    public async Task Expired_untyped_brief_still_cancels_its_optional_task()
    {
        await using var h = await CreateAsync();
        var id = await h.SeedPendingMessageAsync(Body);
        var taskId = await AttachTaskAsync(id, AgentTaskStatus.Dispatched, DateTime.UtcNow.AddMinutes(-1));

        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        (await MessageAsync(id)).Status.ShouldBe(QueuedMessageStatus.Canceled);
        await using var db = CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Canceled);
        task.FailureReason.ShouldBe("Optional work expired before execution.");
        h.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task Send_now_does_not_cancel_an_orphan()
    {
        await using var h = await CreateAsync();
        var id = await h.SeedPendingMessageAsync(Body);
        var taskId = await AttachTaskAsync(id, AgentTaskStatus.Failed);

        await h.Queue.SendNowAsync(h.SessionId, id, CancellationToken.None);

        (await MessageAsync(id)).DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        h.Adapter.Inputs.ShouldBe([Body, "\r"]);
        await using var db = CreateContext();
        (await db.AgentTasks.SingleAsync(t => t.Id == taskId)).Status.ShouldBe(AgentTaskStatus.Failed);
    }

    // ---- CARD-0501 re-review F3: the durable cap binds interrupted-Sent recovery too -----------
    //
    // The crash-boundary case above stops one write short of the worst state: the charge commits,
    // the failure handler never runs, and the row is left Sent with NO verdict AT the attempts cap.
    // LoadInterruptedSentRunAsync selects on Status/verdict/age alone and runs BEFORE the Pending
    // `DeliveryAttempts < MaxAttempts` filter, so the next sweep pressed Enter on a row that had
    // already spent every attempt - 3 attempts became 4, and nothing bounded the one after that.
    // Both entry points reproduced it, so both are pinned.
    private static Task<Guid> SeedCrashedAtCapAsync(BridgeQueueHarness h, long baseline,
        string body = Body, string? conversationKey = null, int attempts = 3,
        QueuedMessageOrigin origin = QueuedMessageOrigin.Delegation,
        DateTime? startedAt = null) =>
        h.SeedPendingMessageAsync(body, deliveryAttempts: attempts, baselineSequence: baseline,
            status: QueuedMessageStatus.Sent, conversationKey: conversationKey, origin: origin,
            lastDeliveryStartedAt: startedAt ?? DateTime.UtcNow - TimeSpan.FromMinutes(4));

    [Test]
    [Arguments("Flush")]
    [Arguments("Sweep")]
    public async Task Crashed_Sent_row_at_the_cap_parks_instead_of_Entering_again(string path)
    {
        await using var h = await CreateAsync();
        var baseline = await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        var id = await SeedCrashedAtCapAsync(h, baseline);
        // The sweep only widens to a session some row brings into scope, and a capped row brings
        // none - which is how the live shape looked: one live brief waiting behind a wedged head.
        const string liveBody = "the next live queue message";
        var liveId = path == "Sweep"
            ? await h.SeedPendingMessageAsync(liveBody, origin: QueuedMessageOrigin.Delegation,
                conversationKey: "task:c501-cap-live")
            : (Guid?)null;
        h.Adapter.PrimeComposer(Body);

        if (path == "Sweep")
            await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
        else
            await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        h.Adapter.Inputs.ShouldBeEmpty("a row at the cap is never Entered and never re-typed");
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
        await AssertUserPromptsAsync(h, []);
        var row = await MessageAsync(id);
        row.DeliveryAttempts.ShouldBe(3, "the cap is durable - recovery may not spend a fourth");
        row.Status.ShouldBe(QueuedMessageStatus.Pending, "reverted to the visible parked shape");
        row.DeliveryVerdict.ShouldBeNull("the crash lost that observation; none is invented");
        (await h.Queue.GetQueueAsync(h.SessionId, CancellationToken.None)).Messages
            .Single(m => m.Id == id).Parked.ShouldBeTrue();
        h.Adapter.Killed.ShouldBeFalse();
        if (liveId is { } live)
        {
            // F1 still owns the composer: the parked body is standing in it, so the live row waits.
            var next = await MessageAsync(live);
            next.DeliveryAttempts.ShouldBe(0);
            next.Status.ShouldBe(QueuedMessageStatus.Pending);
        }
    }

    // A second and third cycle must add nothing. The old behaviour was unbounded, so "it stops" is
    // the claim under test, not merely "it did not fire this once".
    [Test]
    public async Task Repeated_sweeps_over_a_capped_crashed_row_add_no_attempts_and_no_Enters()
    {
        await using var h = await CreateAsync();
        var baseline = await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        var id = await SeedCrashedAtCapAsync(h, baseline);
        h.Adapter.PrimeComposer(Body);

        for (var cycle = 0; cycle < 3; cycle++)
            await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        h.Adapter.Inputs.ShouldBeEmpty();
        (await MessageAsync(id)).DeliveryAttempts.ShouldBe(3);
    }

    // A recovered run is ONE composed body under ONE Enter, so the cap is all-or-nothing across it:
    // a single capped row means the tail must not be submitted either, whatever its own count says.
    [Test]
    public async Task One_capped_row_parks_the_whole_interrupted_batch()
    {
        await using var h = await CreateAsync();
        var baseline = await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        const string first = "batch first body that is long enough";
        const string second = "batch second body that is long enough";
        // One typing, so ONE LastDeliveryStartedAt: that identity is what makes the two rows a run.
        var started = DateTime.UtcNow - TimeSpan.FromMinutes(4);
        var firstId = await SeedCrashedAtCapAsync(h, baseline, first, "task:c501-cap-batch",
            startedAt: started);
        var secondId = await SeedCrashedAtCapAsync(h, baseline, second, "task:c501-cap-batch",
            attempts: 1, startedAt: started);
        h.Adapter.PrimeComposer(ChannelPromptFormat.FormatBatch([first], second));

        await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);

        h.Adapter.Inputs.ShouldBeEmpty();
        await AssertUserPromptsAsync(h, []);
        (await MessageAsync(firstId)).DeliveryAttempts.ShouldBe(3);
        var tail = await MessageAsync(secondId);
        tail.DeliveryAttempts.ShouldBe(1, "the tail keeps its own count - nothing was attempted");
        tail.Status.ShouldBe(QueuedMessageStatus.Pending);
        foreach (var id in new[] { firstId, secondId })
            (await MessageAsync(id)).DeliveryVerdict.ShouldBeNull();
        h.Adapter.Killed.ShouldBeFalse();
    }

    // Control: the cap is a CEILING, not a new refusal. An interrupted row below it still gets the
    // Enter-only rescue this card added, on the same screen evidence.
    [Test]
    public async Task Crashed_Sent_row_below_the_cap_still_recovers_by_Enter_only()
    {
        await using var h = await CreateAsync();
        var baseline = await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        var id = await SeedCrashedAtCapAsync(h, baseline, attempts: 2);
        h.Adapter.PrimeComposer(Body);

        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        h.Adapter.Inputs.ShouldBe(["\r"], "the body is on screen, so it is Entered and not re-typed");
        h.Adapter.SubmittedBodies.ShouldBe([Body]);
        var row = await MessageAsync(id);
        row.Status.ShouldBe(QueuedMessageStatus.Sent);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        row.DeliveryAttempts.ShouldBe(2, "a successful Enter finishes the original typing");
    }

    // Control: late-confirm runs BEFORE the cap gate, so a capped row whose body really did land is
    // still resolved as Sent - the cap withholds the Enter, it does not withhold the evidence.
    [Test]
    public async Task A_capped_crashed_row_whose_body_landed_is_still_late_confirmed()
    {
        await using var h = await CreateAsync();
        var baseline = await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        var id = await SeedCrashedAtCapAsync(h, baseline);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, Body);
        // The turn the prompt started has to end, or the flush defers on the working rule and the
        // case would prove nothing about the cap gate.
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        h.Adapter.PrimeComposer(Body);

        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        h.Adapter.Inputs.ShouldBeEmpty();
        var row = await MessageAsync(id);
        row.Status.ShouldBe(QueuedMessageStatus.Sent);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
        row.DeliveryAttempts.ShouldBe(3);
    }

    // Control: the release side. Once the generation moves (a relaunch), the parked crashed row
    // stops holding the composer and the live row delivers ALONE - its own body, exactly.
    [Test]
    public async Task A_relaunch_releases_the_queue_behind_a_capped_crashed_row()
    {
        await using var h = await CreateAsync();
        var baseline = await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        var id = await SeedCrashedAtCapAsync(h, baseline);
        const string liveBody = "the next live queue message";
        var liveId = await h.SeedPendingMessageAsync(liveBody);
        h.Adapter.PrimeComposer(Body);
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        (await MessageAsync(id)).Status.ShouldBe(QueuedMessageStatus.Pending);

        await AdvanceGenerationAsync(h);
        h.Adapter.PrimeComposer("");
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        h.Adapter.SubmittedBodies.ShouldBe([liveBody]);
        await AssertUserPromptsAsync(h, [liveBody]);
        (await MessageAsync(liveId)).DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        (await MessageAsync(id)).DeliveryAttempts.ShouldBe(3, "still parked, still never re-typed");
    }
}
