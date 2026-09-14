using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
public class SessionMessageQueueWedgedHeadTests
{
    private const string Body = "[antiphon-task:419b8b34] role=Check tier=Low workspace=Shared";
    private static AppDbContext CreateContext() => BridgeQueueHarness.CreateContext();
    private static Task<BridgeQueueHarness> CreateAsync(bool alwaysOn = false) =>
        BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = alwaysOn,
            // These cases judge the recovery verdict, not delayed transcript ingestion.
            ConfigureDeliveryVerification = v => v.PostFailureConfirmGraceSeconds = 0,
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
        var id = await h.SeedPendingMessageAsync(Body, deliveryAttempts: 2);
        const string nextBody = "the next live queue message";
        var nextId = await h.SeedPendingMessageAsync(nextBody);
        h.Adapter.PrimeComposer(Body);
        h.Adapter.SwallowSubmits = 99;

        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        (await MessageAsync(id)).DeliveryAttempts.ShouldBe(3);
        (await h.Queue.GetQueueAsync(h.SessionId, CancellationToken.None)).Messages
            .Single(m => m.Id == id).Parked.ShouldBeTrue();
        h.Adapter.Inputs.ShouldNotContain(nextBody);
        // Model the subsequent fresh composer; the test isolates parking from supervision.
        await AdvanceGenerationAsync(h);
        h.Adapter.PrimeComposer("");
        h.Adapter.SwallowSubmits = 0;
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        h.Adapter.Inputs.ShouldContain(nextBody);
        (await MessageAsync(nextId)).DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        (await MessageAsync(id)).DeliveryAttempts.ShouldBe(3);
    }

    [Test]
    public async Task Failed_Enter_only_recovery_kills_the_generation_it_pressed_Enter_into()
    {
        await using var h = await CreateAsync(alwaysOn: true);
        var generation = await GenerationAsync(h);
        var id = await h.SeedPendingMessageAsync(Body, deliveryAttempts: 1);
        h.Adapter.PrimeComposer(Body);
        h.Adapter.SwallowSubmits = 99;

        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        (await MessageAsync(id)).DeliveryAttempts.ShouldBe(2);
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
        h.Adapter.Inputs.ShouldBe(["\r"]);
        h.Adapter.Killed.ShouldBeFalse();
        h.Adapter.KillGenerationCalls.ShouldBeEmpty();
    }

    private static async Task<Guid> AttachTaskAsync(Guid messageId, AgentTaskStatus status,
        DateTime? deadline = null)
    {
        await using var db = CreateContext();
        var id = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id, RootTaskId = id, Title = "CARD-0501 fixture", Goal = "check fixture",
            Role = AgentTaskRole.Check, Status = status, CreatedAt = DateTime.UtcNow,
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
}
