using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[NotInParallel("MessageQueue")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class BlockedTaskParkReleaseTests
{
    [Test]
    public async Task C1065_ReportPublicationPrecedesPhysicalRelease()
    {
        await using (var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true))
        {
            await f.ReleaseAsync();
            await f.ReleaseFromSettlementAsync();
            f.Wire.ConditionalCommands.ShouldBe(0, "G-95: every Blocked entry requires publication");
            await using var db = f.Db();
            (await db.AgentSessions.SingleAsync(s => s.Id == f.SessionId)).Status.ShouldBe(SessionStatus.Running);
            (await db.RunnerSeatReleases.AnyAsync(r => r.ActionId != null)).ShouldBeFalse("G-95: no reservation without source proof");
        }

        // Actual settlement -> durable notification -> real queue -> native caller prompt.
        // Every cut is crossed with busy-at-enqueue and eligible-at-enqueue recipients.
        foreach (var busy in new[] { false, true })
        foreach (var cut in new[] { "obligation-insert", "settled-committed", "note-insert", "note-committed",
            "render-committed", "attempt-committed", "prompt-accepted" })
        {
            var fault = new C544DeliveryFault();
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true,
                configureDb: b => b.AddInterceptors(fault));
            await f.CreateSourceAsync();
            var parent = await f.AddParentAsync(busy);
            if (cut is "obligation-insert" or "settled-committed" or "note-insert" or "note-committed") fault.Cut = cut;
            await f.SettleAsync("blocked");
            if (cut == "obligation-insert")
            {
                await using var failed = f.Db();
                (await failed.AgentTaskLandNotifications.AnyAsync(n => n.TaskId == f.TaskId)).ShouldBeFalse("H0 rollback");
                f.Wire.ConditionalCommands.ShouldBe(0, "G-94 no release before settlement");
                await f.RestartAsync();
                await f.AttachRecipientAsync(parent);
                await f.Harness.Provider.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(f.SessionId, default);
            }
            var note = await NoteAsync(f);
            note.ConfirmedAt.ShouldBeNull("G-98 queue ACK is not a receipt");
            f.Submitted.ShouldBeEmpty("G-105 enqueue never interrupts caller");
            f.Wire.AtCommand = async command =>
            {
                await using var fresh = f.Db();
                var settled = await fresh.AgentTasks.SingleAsync(t => t.Id == f.TaskId);
                settled.Result.ShouldNotBeNullOrEmpty("G-94 report is committed before wire");
                (await fresh.AgentTaskLandNotifications.SingleAsync(n => n.Id == note.Id)).SourceEventId.ShouldNotBeNull("G-94 obligation is durable");
                var park = await fresh.AgentTaskParks.SingleAsync(p => p.Id == command.ActionId);
                park.State.ShouldBe(AgentTaskParkState.ReleasePending, "G-96 park intent is committed");
                var action = await fresh.RunnerSeatReleases.SingleAsync(r => r.Id == park.RunnerSeatReleaseId);
                action.ActionId.ShouldBe(park.Id, "G-96 persisted publication action identity");
                action.State.ShouldBe(RunnerSeatReleaseState.Unresolved, "G-96 send intent is committed");
                command.ParkVersion.ShouldBe(2);
                command.Publication!.ReceiptId.ShouldBe(park.PublicationReceiptId!.Value);
            };
            f.Clock.Advance(TimeSpan.FromMinutes(3));
            await f.HandleParkAsync();
            (await f.ParkAsync()).State.ShouldBe(AgentTaskParkState.Parked, cut);
            if (cut is "render-committed" or "attempt-committed" or "prompt-accepted")
            {
                fault.Cut = cut;
                if (busy) await f.EndTurnAsync(parent);
                try { await f.FlushAsync(parent); await f.ReconcileParentAsync(); }
                catch (IOException ex) when (ex.Message.StartsWith("c544 completion persistence cut")) { }
            }
            fault.Throws.ShouldBe(1, "G-97 cut reached: " + cut);
            await f.RestartAsync();
            await f.AttachRecipientAsync(parent);
            f.Clock.Advance(TimeSpan.FromMinutes(10));
            await f.ReconcileParentAsync();
            if (busy) await f.EndTurnAsync(parent);
            await f.FlushAsync(parent);
            await f.ReconcileParentAsync();
            var confirmed = await NoteAsync(f);
            confirmed.Id.ShouldBe(note.Id, "G-104 same notification after restart");
            confirmed.ConfirmedAt.ShouldNotBeNull("G-97 whole caller prompt recovers " + cut);
            var rendering = TaskCompletionNotification.TryReadDelivery(confirmed.CompletionDeliveryJson).ShouldNotBeNull();
            await using var verify = f.Db();
            var queue = (await verify.SessionQueuedMessages.Where(m => m.SourceLandNotificationId == note.Id).ToListAsync())
                .ShouldHaveSingleItem("G-104 one queue identity");
            var prompts = await verify.TranscriptEntries.Where(e => e.AgentSessionId == parent
                && e.Kind == TranscriptKinds.UserPrompt && e.Sequence > queue.LastDeliveryBaselineSequence).ToListAsync();
            prompts.Count(e => PromptSubmissionMatch.IsConfirmedBy(rendering.WireText, e.Text)).ShouldBe(1, "G-99/G-100/G-101/G-102 complete recipient receipt");
            f.Submitted.Count.ShouldBe(1, "G-104 no duplicate typed note");
        }

        // Physical recipient proof: real runtime/tailer/generation/input gate, fake child I/O.
        await using (var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true, provider: "Claude"))
        {
            await f.CreateSourceAsync();
            await f.Live!.SubmitAsync("task has a durable blocked checkpoint");
            await f.HandleParkAsync();
            f.Live.Child.Kills.ShouldBe(0);
            f.Live.Clock.Advance(TimeSpan.FromSeconds(120));
            await f.HandleParkAsync();
            (await f.ParkAsync()).State.ShouldBe(AgentTaskParkState.Parked);
            f.Live.Child.Kills.ShouldBe(1, "runner recipient exited");
            f.Live.Runtime.List().ShouldBeEmpty("runner capacity and manifest removed");
            File.Exists(f.Live.TranscriptPath).ShouldBeTrue("native conversation retained");
        }

        foreach (var cut in new[] { "BeforeReservation", "BeforeDispatch", "BeforeResponse", "lost-reply" })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
            await f.CreateSourceAsync();
            // First stop at the ordinary idle refusal, after real publication has persisted.
            f.Wire.Qualified = f.Wire.Qualified with { Status = TerminalSeatQualificationStatus.Working };
            await f.HandleParkAsync();
            var park = await f.ParkAsync();
            park.State.ShouldBe(AgentTaskParkState.Published);
            f.Wire.Qualified = f.Wire.Qualified with { Status = TerminalSeatQualificationStatus.Qualified };
            f.Wire.DropReply = cut == "lost-reply";
            var hit = false;
            try
            {
                await f.ReleaseAsync((boundary, _) =>
                {
                    if (boundary == cut && !hit) { hit = true; throw new IOException("release cut"); }
                    return Task.CompletedTask;
                });
            }
            catch (IOException ex) when (ex.Message == "release cut") { }
            if (cut != "lost-reply") hit.ShouldBeTrue(cut);
            await f.RestartAsync();
            f.Wire.DropReply = false;
            if (cut is "BeforeResponse" or "lost-reply")
            {
                f.Directory.Inventory = () => Task.FromResult<Antiphon.Server.Application.Interfaces.RunnerInventory>(
                    new Antiphon.Server.Application.Interfaces.RunnerInventory.Available([]));
                await File.WriteAllTextAsync(Path.Combine(f.SourcePath, "dirt.txt"), "unpublished");
                await f.HandleParkAsync();
                (await f.ParkAsync()).State.ShouldBe(AgentTaskParkState.ReleasePending, "absent runner cannot waive source checks");
                File.Delete(Path.Combine(f.SourcePath, "dirt.txt"));
            }
            await f.HandleParkAsync();
            (await f.ParkAsync()).Id.ShouldBe(park.Id);
            (await f.ParkAsync()).State.ShouldBe(AgentTaskParkState.Parked, cut);
            f.Wire.Requests.ShouldAllBe(r => r.ActionId == park.Id && r.ParkVersion == 2 && r.Publication != null);
        }
    }

    [Test]
    public async Task C1065_EachBlockCauseUsesCurrentIdleProof()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
        await f.EditAsync((task, _) => { task.CompletedAt = null; task.Result = null; });
        using (var scope = f.Harness.Provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TerminalRunnerSeatReleaseService>()
                .TryHandleTaskAsync(f.TaskId, default);
        await using var db = f.Db();
        (await db.AgentTaskParks.CountAsync(p => p.TaskId == f.TaskId)).ShouldBe(1,
            "G-106: a nonreport block registers an episode without inventing a completion");
        (await f.TaskAsync()).CompletedAt.ShouldBeNull("G-106: no invented completion");
        (await f.TaskAsync()).Result.ShouldBeNull("G-106: no invented report");
        f.Wire.ConditionalCommands.ShouldBe(0, "G-108: unknown source ownership cannot release");
        f.Launches.Calls.ShouldBeEmpty();

        foreach (var cause in new[] { "question", "bind-refusal", "unmarked", "prerequisite", "quota", "wall", "cost", "merge-back", "land-conflict" })
        {
            await using var world = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
            await world.CreateSourceAsync();
            await world.EditAsync((task, _) =>
            {
                task.Result = null; task.CompletedAt = null; task.FailureReason = cause;
                if (cause == "quota") task.FailureCode = AgentTaskFailureCode.SubscriptionQuotaExceeded;
            });
            foreach (var verdict in new[] { TerminalSeatQualificationStatus.Working, TerminalSeatQualificationStatus.Unknown })
            {
                world.Wire.Qualified = world.Wire.Qualified with { Status = verdict };
                await world.HandleParkAsync();
                world.Wire.ConditionalCommands.ShouldBe(0, cause + ": current " + verdict);
            }
            world.Wire.Qualified = world.Wire.Qualified with { Status = TerminalSeatQualificationStatus.Qualified };
            await world.HandleParkAsync();
            (await world.ParkAsync()).State.ShouldBe(AgentTaskParkState.Parked, "G-106 " + cause);
            (await world.ParkAsync()).ReportDigest.ShouldNotBeNullOrEmpty("durable nonreport transcript checkpoint");
            (await world.TaskAsync()).Result.ShouldBeNull();
            (await world.TaskAsync()).CompletedAt.ShouldBeNull();
            world.Launches.Calls.ShouldBeEmpty();
        }
        foreach (var cause in new[] { "create", "routing", "kind-exhausted" })
        {
            await using var world = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
            await world.EditAsync((task, _) => { task.AgentSessionId = null; task.CompletedAt = null; task.Result = null; task.FailureReason = cause; });
            await world.HandleParkAsync();
            (await world.ParkAsync()).SessionId.ShouldBeNull();
            world.Wire.ConditionalCommands.ShouldBe(0, "G-107 " + cause);
            world.Launches.Calls.ShouldBeEmpty("G-107 no invented session");
        }
        foreach (var refusal in new[] { "foreign-writer", "unknown-owner", "sequencer" })
        {
            await using var world = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
            await world.CreateSourceAsync();
            await using var store = world.Db();
            if (refusal == "foreign-writer")
            {
                store.AgentTasks.Add(new AgentTask { Id = Guid.NewGuid(), Status = AgentTaskStatus.Working, WorkingDirectory = world.SourcePath });
                await store.SaveChangesAsync();
            }
            if (refusal == "unknown-owner") await world.EditAsync((task, _) => task.ProgressBaselineJson = null);
            if (refusal == "sequencer")
            {
                var path = await world.GitAsync(world.SourcePath, "rev-parse", "--git-path", "MERGE_HEAD");
                await File.WriteAllTextAsync(path, await world.GitAsync(world.SourcePath, "rev-parse", "HEAD"));
            }
            await world.HandleParkAsync();
            world.Wire.ConditionalCommands.ShouldBe(0, "G-108 " + refusal);
            (await world.ParkAsync()).State.ShouldNotBe(AgentTaskParkState.Parked);
        }
    }

    [Test]
    public async Task C1065_ParkedAgentIsReservedButNotWarm()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
        await f.EditAsync((task, _) => task.Workspace = WorkspaceMode.Shared);
        await f.ReleaseFromSettlementAsync();
        await using var db = f.Db();
        var agent = await db.Agents.SingleAsync(a => a.Id == f.AgentId);
        agent.PoolIdleSince.ShouldBeNull("G-110: blocked parking cannot put an owned identity in the warm pool");
        agent.PoolReservedForRootTaskId.ShouldBeNull("G-110: parked ownership is not a warm reservation");
        f.Wire.ForceCommands.ShouldBe(0);
        f.RecordedStops.Killed.ShouldBeEmpty();

        foreach (var owner in new[] { "none", "standing", "always-on", "board", "specialist", "failed-stop" })
        {
            await using var world = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
            await world.CreateSourceAsync(WorkspaceMode.Shared);
            Guid? boardId = null;
            if (owner == "board")
            {
                await using var boardDb = world.Db();
                var project = new Project { Id = Guid.NewGuid(), Name = "park owner" };
                var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "park owner" };
                boardDb.AddRange(project, board);
                await boardDb.SaveChangesAsync();
                boardId = board.Id;
            }
            await world.EditAsync((task, retained) =>
            {
                if (owner == "standing") retained.IsPoolDelegate = false;
                if (owner == "always-on") retained.AlwaysOn = true;
                if (owner == "board") retained.BoardId = boardId;
                if (owner == "specialist") task.Role = AgentTaskRole.Check;
            });
            if (owner == "failed-stop") world.Wire.Outcome = TerminalSeatReleaseOutcome.Unresolved;
            await world.HandleParkAsync();
            var park = await world.ParkAsync();
            if (owner == "none") park.State.ShouldBe(AgentTaskParkState.Parked);
            if (owner is "standing" or "always-on" or "board" or "specialist")
                world.Wire.ConditionalCommands.ShouldBe(0, "G-113/G-114/G-115/G-116 " + owner);
            await using var store = world.Db();
            var retainedAgent = (await store.Agents.SingleOrDefaultAsync(a => a.Id == world.AgentId)).ShouldNotBeNull("G-109 retained agent");
            retainedAgent.PoolIdleSince.ShouldBeNull("G-110 not warm");
            retainedAgent.PoolReservedForRootTaskId.ShouldBeNull("G-110 not a warm reservation");
            if (owner == "none") retainedAgent.Status.ShouldBe(AgentStatus.Stopped);
            if (owner == "failed-stop")
                (await store.AgentSessions.SingleAsync(s => s.Id == world.SessionId)).Status.ShouldBe(SessionStatus.Running);
            if (owner != "none") continue;

            // Independently challenge pool and retirement reservations with an otherwise reusable row.
            await world.EditAsync((_, a) => { a.Status = AgentStatus.Idle; a.PoolIdleSince = world.Now.AddDays(-2); });
            using (var scope = world.Harness.Provider.CreateScope())
            {
                var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
                var unrelated = new AgentTask { Id = Guid.NewGuid(), AgentId = world.AgentId, Workspace = WorkspaceMode.Shared };
                (await dispatcher.TryReuseWarmAgentAsync(unrelated, world.Now, default)).ShouldBe(AgentTaskDispatcher.ReuseOutcome.WaitForAgent, "G-111 reservation vetoes admission");
                await dispatcher.RetireIdleWarmAgentsAsync(default);
            }
            (await store.Agents.AsNoTracking().AnyAsync(a => a.Id == world.AgentId)).ShouldBeTrue("G-112 retirement keeps reserved identity");
            using (var scope = world.Harness.Provider.CreateScope())
                await scope.ServiceProvider.GetRequiredService<AgentTaskService>().RemoveEphemeralAgentAsync(await world.TaskAsync(), world.AgentId, default);
            (await store.Agents.AsNoTracking().AnyAsync(a => a.Id == world.AgentId)).ShouldBeTrue("G-109 generic removal keeps identity");

            // Closing this attempt does not clear a different task's reservation on the identity.
            var other = new AgentTask { Id = Guid.NewGuid(), AgentId = world.AgentId, Attempt = 1, Status = AgentTaskStatus.Blocked };
            store.AgentTasks.Add(other);
            store.AgentTaskParks.Add(new AgentTaskPark { Id = Guid.NewGuid(), TaskId = other.Id, Attempt = 1,
                AgentId = world.AgentId, BlockEventId = Guid.NewGuid(), State = AgentTaskParkState.Requested });
            await store.SaveChangesAsync();
            await world.EditAsync((task, _) => task.Status = AgentTaskStatus.Canceled);
            (await PoolDelegateRelease.Reservations(store).Where(p => p.AgentId == world.AgentId).Select(p => p.TaskId).ToListAsync())
                .ShouldBe(new[] { other.Id }, "G-117 cancellation releases only its own reservation");
        }
    }

    private static async Task<AgentTaskLandNotification> NoteAsync(RunnerSeatReleaseFixture f)
    {
        await using var db = f.Db();
        return await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.TaskId == f.TaskId);
    }
}
