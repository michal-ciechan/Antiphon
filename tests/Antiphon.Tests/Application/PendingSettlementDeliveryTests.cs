using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1082 F3c D2. A lease-busy settlement's caller note is accepted as one complete
/// UserPrompt. Pending uses the profiled completion obligation. Blocked uses the direct
/// parent note. The Held re-check does not enqueue a caller note.
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
}
