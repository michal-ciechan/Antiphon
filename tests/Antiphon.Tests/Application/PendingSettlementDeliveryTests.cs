using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1082 F3c D2 and F3d. A lease-busy settlement's caller note is accepted as one complete
/// UserPrompt. Pending uses the profiled completion obligation. Blocked uses the direct
/// parent note. A lost enqueue acknowledgement and a receipt-save failure each recover that
/// one prompt. The Held re-check does not enqueue a caller note.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class PendingSettlementDeliveryTests
{
    /// <summary>
    /// The producer text reaches the caller transcript exactly once: an idle recipient, a
    /// recipient that is busy and then idle, and a crash between the queue insert and
    /// confirmation. A second flush does not add a prompt. Pending stays Pending.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    [Arguments("pending", "eligible")]
    [Arguments("pending", "busy")]
    [Arguments("pending", "crash")]
    [Arguments("blocked", "eligible")]
    [Arguments("blocked", "busy")]
    [Arguments("blocked", "crash")]
    public async Task C1082_LeaseBusySettlementNoteIsAcceptedComplete(string producer, string delivery)
    {
        var pending = producer == "pending";
        await using var world = await RunnerSettlementWorld.CreateAsync(
            profiled: pending, controlledSyncClock: true);
        if (pending)
        {
            var source = await world.Git.RunnerPushAsync("work.txt", "runner work");
            await world.Git.RunAsync(world.Git.Desktop, "fetch", "--no-tags", "origin", world.Git.FullRef);
            (await world.Git.HasObjectAsync(source)).ShouldBeTrue();
        }
        else
            await world.Git.RunnerPushAsync("work.txt", "runner work");

        await using (var held = await world.Git.Leases.TryAcquireAsync(world.Git.Desktop, CancellationToken.None))
        {
            held.ShouldNotBeNull();
            var settle = world.SettleAsync(RunnerSettlementWorld.Report("Implemented and pushed."));
            (await Task.WhenAny(world.LeaseBusy.First, settle)).ShouldBe(world.LeaseBusy.First,
                producer + " must wait on the busy lease");
            world.SyncClock!.Advance(TimeSpan.FromSeconds(new DelegationSettings().RunnerSyncBudgetSeconds));
            await settle;
        }

        var queued = (await world.NoteAsync()).ShouldNotBeNull();
        queued.Status.ShouldBe(QueuedMessageStatus.Pending);
        queued.DeliveryAttempts.ShouldBe(0);
        if (pending)
        {
            world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, producer);
            queued.Body.ShouldContain("desktop-sync=pending");
            queued.Body.ShouldContain("synced later");
            queued.Body.ShouldContain("No reply is needed");
            queued.Body.ShouldNotContain("then reply");
        }
        else
        {
            world.Task.Status.ShouldBe(AgentTaskStatus.Blocked, producer);
            queued.Body.ShouldContain("Runner sync unavailable: " + RemoteSettlementSyncReasons.LeaseBusy);
            queued.Body.ShouldContain("then reply");
            queued.Body.ShouldNotContain("synced later");
            queued.Body.ShouldNotContain("desktop-sync=pending");
        }

        await using var bridge = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = world.Schema.ConnectionString,
            Delegation = new DelegationSettings(),
        });
        await QueuedReceiptAssertions.ConfirmQueuedReceiptAsync(
            world.Schema.ConnectionString, bridge, queued, world.CallerSessionId,
            busy: delivery == "busy",
            cut: delivery == "crash" ? "queue-inserted" : "after-receipt");

        await using var verify = world.CreateContext();
        var prompts = await verify.TranscriptEntries.AsNoTracking()
            .Where(e => e.AgentSessionId == world.CallerSessionId && e.Kind == TranscriptKinds.UserPrompt)
            .ToListAsync();
        var prompt = prompts.ShouldHaveSingleItem();
        PromptSubmissionMatch.IsCompleteIn(queued.Body, prompt.Text!).ShouldBeTrue(producer + "/" + delivery);
        var still = await verify.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == world.CallerSessionId && m.SourceTaskId == world.TaskId)
            .SingleAsync();
        still.Id.ShouldBe(queued.Id);
        if (pending)
        {
            var debt = await verify.AgentTaskSyncDebts.AsNoTracking().SingleAsync(d => d.TaskId == world.TaskId);
            debt.State.ShouldBe(AgentTaskSyncDebtState.Pending);
            debt.ConfirmedSha.ShouldBeNull();
        }
        else
            (await verify.AgentTaskSyncDebts.CountAsync(d => d.TaskId == world.TaskId)).ShouldBe(0);

        await bridge.Queue.FlushIfIdleAsync(world.CallerSessionId, CancellationToken.None);
        await using var after = world.CreateContext();
        (await after.TranscriptEntries.CountAsync(e =>
            e.AgentSessionId == world.CallerSessionId && e.Kind == TranscriptKinds.UserPrompt)).ShouldBe(1);
    }

    /// <summary>
    /// The queue insert commits and its acknowledgement is lost. Recovery links that same row
    /// and the caller still accepts exactly one complete UserPrompt.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task C1082_LostEnqueueAckRecoversOnePendingUserPrompt()
    {
        var boundary = new LostEnqueueAck();
        await using var world = await SettlePendingAsync(boundary);
        boundary.QueueInserted.ShouldBe(1, "the committed insert's acknowledgement is the lost ack");

        await using var afterFault = world.CreateContext();
        var note = await CompletionAsync(afterFault, world.TaskId);
        var sourceEvent = note.SourceEventId;
        note.ConfirmedAt.ShouldBeNull();
        note.QueueMessageId.ShouldBeNull();
        note.State.ShouldBe(LandNotificationState.RetryPending);
        note.LastErrorCode.ShouldNotBeNull().ShouldContain("notification_reconcile_failed");
        var queued = (await afterFault.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.SourceLandNotificationId == note.Id).ToListAsync()).ShouldHaveSingleItem();
        queued.SourceTaskId.ShouldBe(world.TaskId);

        await afterFault.AgentTaskLandNotifications.Where(n => n.Id == note.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.NextAttemptAt, DateTime.UtcNow.AddMinutes(-1)));
        await using var recovered = world.CreateContext();
        await ReconcileAsync(world, recovered, note.Id, boundary: null);
        var linked = await CompletionAsync(recovered, world.TaskId);
        linked.Id.ShouldBe(note.Id);
        linked.QueueMessageId.ShouldBe(queued.Id);
        linked.SourceEventId.ShouldBe(sourceEvent);
        linked.ConfirmedAt.ShouldBeNull();
        linked.State.ShouldBe(LandNotificationState.AwaitingReceipt);
        (await recovered.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == note.Id)).ShouldBe(1);

        await using var bridge = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = world.Schema.ConnectionString,
            Delegation = new DelegationSettings(),
        });
        var row = await recovered.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == queued.Id);
        await QueuedReceiptAssertions.ConfirmQueuedReceiptAsync(
            world.Schema.ConnectionString, bridge, row, world.CallerSessionId, busy: false);
        await AssertOnePendingPromptAsync(world, bridge, row, note.Id, queued.Id, sourceEvent);
    }

    /// <summary>
    /// The receipt is found and the save of ConfirmedAt fails. The next reconcile confirms the
    /// same notification and does not add a second UserPrompt.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task C1082_ReceiptSaveFailureRecoversOnePendingUserPrompt()
    {
        await using var world = await SettlePendingAsync(boundary: null);
        var queued = (await world.NoteAsync()).ShouldNotBeNull();
        await using var bridge = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = world.Schema.ConnectionString,
            Delegation = new DelegationSettings(),
        });
        await QueuedReceiptAssertions.ConfirmQueuedReceiptAsync(
            world.Schema.ConnectionString, bridge, queued, world.CallerSessionId, busy: false);

        await using var prepared = world.CreateContext();
        var note = await CompletionAsync(prepared, world.TaskId);
        var sourceEvent = note.SourceEventId;
        note.QueueMessageId.ShouldBe(queued.Id);
        note.ConfirmedAt.ShouldBeNull();
        note.CompletionDeliveryJson.ShouldNotBeNullOrWhiteSpace();
        (await prepared.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == queued.Id))
            .DeliveryAttempts.ShouldBeGreaterThan(0);
        (await UserPromptCountAsync(prepared, world.CallerSessionId)).ShouldBe(1);

        var fault = new ReceiptSaveFailure();
        await using var faultDb = world.CreateContext();
        await ReconcileAsync(world, faultDb, note.Id, fault);
        fault.ReceiptSaveReached.ShouldBeTrue();
        (await CompletionAsync(faultDb, world.TaskId)).ConfirmedAt.ShouldBeNull();
        (await UserPromptCountAsync(faultDb, world.CallerSessionId)).ShouldBe(1);

        await using var recovered = world.CreateContext();
        await ReconcileAsync(world, recovered, note.Id, boundary: null);
        await AssertOnePendingPromptAsync(world, bridge, queued, note.Id, queued.Id, sourceEvent);
        var saved = await CompletionAsync(recovered, world.TaskId);
        saved.State.ShouldBe(LandNotificationState.Confirmed);
        saved.ConfirmingPromptSequence.ShouldNotBeNull();
        saved.QueueMessageId.ShouldBe(queued.Id);
        saved.SourceEventId.ShouldBe(sourceEvent);
    }

    private static async Task<RunnerSettlementWorld> SettlePendingAsync(LandDeliveryBoundary? boundary)
    {
        var world = await RunnerSettlementWorld.CreateAsync(profiled: true, controlledSyncClock: true);
        if (boundary is not null)
        {
            world.ConfigureServices = services => services.AddSingleton(boundary);
            await world.RestartServicesAsync();
        }

        var source = await world.Git.RunnerPushAsync("work.txt", "runner work");
        await world.Git.RunAsync(world.Git.Desktop, "fetch", "--no-tags", "origin", world.Git.FullRef);
        (await world.Git.HasObjectAsync(source)).ShouldBeTrue();
        await using (var held = await world.Git.Leases.TryAcquireAsync(world.Git.Desktop, CancellationToken.None))
        {
            held.ShouldNotBeNull();
            var settle = world.SettleAsync(RunnerSettlementWorld.Report("Implemented and pushed."));
            (await Task.WhenAny(world.LeaseBusy.First, settle)).ShouldBe(world.LeaseBusy.First,
                "pending settlement must wait on the busy lease");
            world.SyncClock!.Advance(TimeSpan.FromSeconds(new DelegationSettings().RunnerSyncBudgetSeconds));
            await settle;
        }

        world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded);
        return world;
    }

    private static async Task ReconcileAsync(
        RunnerSettlementWorld world, AppDbContext db, Guid noteId,
        LandDeliveryBoundary? boundary)
    {
        var service = new AgentTaskLandNotificationService(
            db,
            world.Services.GetRequiredService<SessionMessageQueueService>(),
            world.Services.GetRequiredService<CompletionNoteFlushQueue>(),
            world.Services.GetRequiredService<AgentSessionRuntime>(),
            TimeProvider.System,
            boundary);
        await service.ReconcileAsync(noteId, CancellationToken.None);
    }

    private static Task<AgentTaskLandNotification> CompletionAsync(AppDbContext db, Guid taskId) =>
        db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n =>
            n.TaskId == taskId && n.Kind == LandNotificationKind.TaskCompletion);

    private static Task<int> UserPromptCountAsync(AppDbContext db, Guid sessionId) =>
        db.TranscriptEntries.CountAsync(e =>
            e.AgentSessionId == sessionId && e.Kind == TranscriptKinds.UserPrompt);

    private static async Task AssertOnePendingPromptAsync(
        RunnerSettlementWorld world, BridgeQueueHarness bridge, SessionQueuedMessage queued,
        Guid noteId, Guid queueId, Guid sourceEvent)
    {
        await using var verify = world.CreateContext();
        var prompts = await verify.TranscriptEntries.AsNoTracking()
            .Where(e => e.AgentSessionId == world.CallerSessionId && e.Kind == TranscriptKinds.UserPrompt)
            .ToListAsync();
        var prompt = prompts.ShouldHaveSingleItem();
        PromptSubmissionMatch.IsCompleteIn(queued.Body, prompt.Text!).ShouldBeTrue();
        var note = await CompletionAsync(verify, world.TaskId);
        note.Id.ShouldBe(noteId);
        note.QueueMessageId.ShouldBe(queueId);
        note.SourceEventId.ShouldBe(sourceEvent);
        (await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == queueId)).Id.ShouldBe(queueId);
        var debt = await verify.AgentTaskSyncDebts.AsNoTracking().SingleAsync(d => d.TaskId == world.TaskId);
        debt.State.ShouldBe(AgentTaskSyncDebtState.Pending);
        debt.ConfirmedSha.ShouldBeNull();

        await bridge.Queue.FlushIfIdleAsync(world.CallerSessionId, CancellationToken.None);
        await using var after = world.CreateContext();
        (await UserPromptCountAsync(after, world.CallerSessionId)).ShouldBe(1);
        (await after.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == noteId)).ShouldBe(1);
    }

    private sealed class LostEnqueueAck : LandDeliveryBoundary
    {
        public int QueueInserted { get; private set; }

        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary != "queue-inserted")
                return Task.CompletedTask;
            QueueInserted++;
            return Task.FromException(new IOException("owned queue-ack failure"));
        }
    }

    private sealed class ReceiptSaveFailure : LandDeliveryBoundary
    {
        public bool ReceiptSaveReached { get; private set; }

        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary != "receipt-before-save")
                return Task.CompletedTask;
            ReceiptSaveReached = true;
            return Task.FromException(new IOException("owned receipt save failure"));
        }
    }
}
