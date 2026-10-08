using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1151 S5: the brief owner boundary (CARD-1150 S2). Design V-10. Fixture: the isolated
/// PostgreSQL schema and the <c>BridgeQueueHarness</c>/<c>OpenSweep</c> shapes of this partial
/// class, a Working prompt-only task at nine minutes, the real <c>TickAsync</c> repeated through
/// detection (8 min) and operator escalation (20 min) on a <c>FakeTimeProvider</c>. Helper names
/// in this file carry the <c>BootStall</c> prefix so they cannot collide with the S2 repair's
/// <c>Brief*</c> partials.
/// </summary>
public partial class DelegationDispatchRecoveryBoundaryTests
{
    /// <summary>
    /// V-10. inline: the Sent delegation brief row. spilled: the Sent row plus its spill file.
    /// pending-ui-followup: a Pending Ui row queued behind the Sent brief. Decisive: SHA-256 of
    /// Body, RemoteSpillBody and the spill file bytes, plus Id, Sequence, DeliveryAttempts,
    /// Status and the task's Attempt/DispatchedAt/ConcurrencyToken, equal before the first
    /// detection tick and after the escalation tick; zero new SessionQueuedMessages rows on the
    /// delegate session; runner Inputs 0; no ensure/send call recorded.
    /// </summary>
    [Test]
    [Arguments("inline")]
    [Arguments("spilled")]
    [Arguments("pending-ui-followup")]
    public Task C1151_Brief_and_spill_remain_byte_identical(string shape) =>
        Card1151Pending.Skip("S5", nameof(C1151_Brief_and_spill_remain_byte_identical));

    /// <summary>
    /// CARD-1151 R3. The role-ceiling failure is the failure path CARD-1151 keeps (and R1 restores
    /// for queued-only input), so its caller note is proved here end to end rather than borrowed
    /// from another producer: the real <c>FailOverdueTasksAsync</c> fails an idle Code task past
    /// the 240-minute ceiling through <c>FailAndNotifyAsync</c>, which enqueues the note on the real
    /// queue, and the parent session's transcript then carries exactly one complete matching
    /// <c>UserPrompt</c> with the note's durable SourceTaskId and a Delivered verdict. eligible: the
    /// idle parent takes it on the next flush. busy: a Working parent submits nothing until its
    /// TurnEnd. crash: the parent's harness is torn down and re-attached after the enqueue. A
    /// second flush submits nothing more; the decoy session receives nothing.
    /// </summary>
    [Test]
    [Arguments("eligible")]
    [Arguments("busy")]
    [Arguments("crash")]
    public async Task C1151_Ceiling_failure_note_has_one_complete_user_prompt(string mode)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var sweepClock = new FakeTimeProvider(DateTimeOffset.UtcNow);
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
            var taskId = await BootStallSeedCeilingTaskAsync(
                schema.ConnectionString, parent.SessionId, sweepClock.GetUtcNow().UtcDateTime);
            var runner = new CountingRunner();
            var stopper = new RecordingSessionStopper();
            await using (var host = OpenSweep(
                schema.ConnectionString, runner, stopper, sweepClock, new DeadSessionFirstSeenState()))
                (await host.OverdueAsync()).ShouldBe(1, mode);

            SessionQueuedMessage note;
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                var failed = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
                failed.Status.ShouldBe(AgentTaskStatus.Failed, mode);
                failed.FailureReason.ShouldNotBeNull(mode);
                failed.FailureReason.ShouldContain("240-minute ceiling for role Code", customMessage: mode);
                note = (await db.SessionQueuedMessages.AsNoTracking()
                    .Where(m => m.SourceTaskId == taskId).ToListAsync()).ShouldHaveSingleItem(mode);
                note.AgentSessionId.ShouldBe(parent.SessionId, mode);
                note.Origin.ShouldBe(QueuedMessageOrigin.Delegation, mode);
                note.Body.ShouldContain(DelegationReportFormatter.Short(taskId), customMessage: mode);
                note.Body.ShouldContain("240-minute ceiling", customMessage: mode);
            }

            var floor = await PromptFloorAsync(schema.ConnectionString, parent.SessionId);
            if (mode == "crash")
            {
                var sessionId = parent.SessionId;
                var agentId = parent.AgentId;
                await parent.DisposeAsync();
                parent = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
                {
                    AlwaysOn = true,
                    PreserveDatabaseOnDispose = true,
                    ConnectionString = schema.ConnectionString,
                    AttachSessionId = sessionId,
                    AttachAgentId = agentId,
                });
            }

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
            stopper.Killed.ShouldBeEmpty("the ceiling failure never stops the delegate");
            runner.Kills.ShouldBe(0, mode);
            runner.Inputs.ShouldBe(0, mode);
        }
        finally
        {
            await parent.DisposeAsync();
            await decoy.DisposeAsync();
        }
    }

    /// <summary>
    /// A live, idle delegate (Running row; its prompt answered by a TurnEnd, so no boot episode)
    /// whose Code task was dispatched 241 minutes before <paramref name="now"/> and replies to
    /// <paramref name="parentId"/>.
    /// </summary>
    private static async Task<Guid> BootStallSeedCeilingTaskAsync(string connection, Guid parentId, DateTime now)
    {
        var taskId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var dispatched = Pg(now.AddMinutes(-241));
        var cwd = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "antiphon-c1151-r3", taskId.ToString("N"))).FullName;
        var name = $"c1151-{agentId:N}"[..16];
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "ceiling-failure",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = cwd,
            CreatedAt = dispatched,
            StartedAt = dispatched,
            LastSeenAt = Pg(now),
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = name,
            Slug = name,
            WorkingDirectory = cwd,
            Details = "CARD-1151 R3 ceiling-failure producer.",
            Status = AgentStatus.Running,
            Kind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.High,
            IsPoolDelegate = false,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = dispatched,
            UpdatedAt = dispatched,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            Title = "Ceiling failure note",
            Goal = "Run past the role ceiling.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.High,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = cwd,
            Status = AgentTaskStatus.Working,
            Attempt = 1,
            ReplyTo = AgentTaskReplyTo.Session,
            ParentSessionId = parentId,
            CreatedAt = dispatched,
            DispatchedAt = dispatched,
            AgentId = agentId,
            AgentSessionId = sessionId,
        });
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = 1,
            Kind = TranscriptKinds.UserPrompt,
            Text = $"{DelegationReportFormatter.TaskMarker(taskId)} the brief",
            Timestamp = Pg(dispatched.AddMinutes(1)),
            CreatedAt = Pg(dispatched.AddMinutes(1)),
        });
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = 2,
            Kind = TranscriptKinds.TurnEnd,
            StopReason = "end_turn",
            Timestamp = Pg(dispatched.AddMinutes(2)),
            CreatedAt = Pg(dispatched.AddMinutes(2)),
        });
        await db.SaveChangesAsync();
        return taskId;
    }
}
