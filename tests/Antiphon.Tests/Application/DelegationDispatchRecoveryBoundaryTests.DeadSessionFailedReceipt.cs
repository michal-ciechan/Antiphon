using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Antiphon.Tests.Application;

public partial class DelegationDispatchRecoveryBoundaryTests
{
    /// <summary>
    /// CARD-1161 repair. The ordinary dead-session Failed note (a session reason other than
    /// the absent-launch hold) goes through the real queue, the way the Blocked note does.
    /// eligible: an idle caller gets one complete UserPrompt and that receipt closes the row.
    /// busy: the row stays queued, then one flush after TurnEnd delivers it once.
    /// </summary>
    [Test]
    [Arguments("eligible")]
    [Arguments("busy")]
    public async Task C1161_Failed_caller_note_has_one_complete_user_prompt(string mode)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        // The sweep clock jumps the dead-session grace. Delivery confirmation waits on
        // TimeProvider.Delay, and a frozen clock does not release that wait, so the caller
        // harness keeps the system clock. The note is written about two minutes before it.
        var sweepClock = new FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-5));
        var parent = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            PreserveDatabaseOnDispose = true,
            ConnectionString = schema.ConnectionString,
        });
        var decoy = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            PreserveDatabaseOnDispose = true,
            ConnectionString = schema.ConnectionString,
        });
        try
        {
            var seeded = await SeedAsync(schema.ConnectionString, new AbsentShape
            {
                SessionReason = "process vanished",
                ExistingParentId = parent.SessionId,
                Brief = false,
            });
            var runner = new CountingRunner();
            var stopper = new RecordingSessionStopper();
            await using (var host = OpenSweep(
                schema.ConnectionString, runner, stopper, sweepClock, new DeadSessionFirstSeenState()))
                await host.DueAsync();

            // enqueue-failure recovery is not covered: CARD-1167

            SessionQueuedMessage note;
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                var failed = await db.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
                failed.Status.ShouldBe(AgentTaskStatus.Failed, mode);
                failed.CompletedAt.ShouldNotBeNull(mode);
                failed.FailureReason.ShouldNotBeNull(mode);
                failed.FailureReason!.Contains("process vanished", StringComparison.Ordinal).ShouldBeTrue(mode);
                failed.FailureReason.StartsWith("dispatch_launch_absent", StringComparison.Ordinal).ShouldBeFalse(mode);
                (await db.AgentTaskEvents.CountAsync(e =>
                        e.AgentTaskId == seeded.TaskId && e.Type == AgentTaskEventType.Failed))
                    .ShouldBe(1, mode);
                (await db.AgentTaskEvents.CountAsync(e =>
                        e.AgentTaskId == seeded.TaskId && e.Type == AgentTaskEventType.Blocked))
                    .ShouldBe(0, mode);
                note = (await db.SessionQueuedMessages.AsNoTracking()
                    .Where(m => m.SourceTaskId == seeded.TaskId).ToListAsync()).ShouldHaveSingleItem(mode);
                note.AgentSessionId.ShouldBe(parent.SessionId, mode);
                note.ExecutionTaskId.ShouldBeNull(mode);
                note.Origin.ShouldBe(QueuedMessageOrigin.Delegation, mode);
                note.Body.ShouldContain("process vanished", customMessage: mode);
            }

            var floor = await PromptFloorAsync(schema.ConnectionString, parent.SessionId);
            if (mode == "busy")
            {
                await parent.MarkWorkingAsync();
                await parent.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
                parent.Adapter.SubmittedBodies.ShouldBeEmpty(mode);
                (await PromptsAfterAsync(schema.ConnectionString, parent.SessionId, floor)).ShouldBeEmpty(mode);
                await parent.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
                await parent.Queue.OnTurnEndAsync(parent.SessionId, CancellationToken.None);
            }
            else
            {
                await parent.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
            }

            await AssertOneReceiptAsync(schema.ConnectionString, note, parent.SessionId, floor, mode);
            parent.Adapter.SubmittedBodies.Count.ShouldBe(1, mode);
            await parent.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
            await AssertOneReceiptAsync(schema.ConnectionString, note, parent.SessionId, floor, mode + "-once");
            parent.Adapter.SubmittedBodies.Count.ShouldBe(1, mode + "-once");
            (await PromptsAfterAsync(schema.ConnectionString, decoy.SessionId, 0)).ShouldBeEmpty(mode);
            decoy.Adapter.SubmittedBodies.ShouldBeEmpty(mode);
            (await CountNotesAsync(schema.ConnectionString, seeded.TaskId)).ShouldBe(1, mode + "-note");
            Quiet(runner, stopper, mode);
        }
        finally
        {
            await parent.DisposeAsync();
            await decoy.DisposeAsync();
        }
    }

    private static async Task<int> CountNotesAsync(string connection, Guid taskId)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        return await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == taskId);
    }
}
