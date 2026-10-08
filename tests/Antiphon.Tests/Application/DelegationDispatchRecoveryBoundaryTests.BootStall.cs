using System.Security.Cryptography;
using System.Text;
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
/// PostgreSQL schema and the shared <see cref="BootStallWorld"/> (its graph carries the real
/// queue service), a Working prompt-only task at nine minutes, the real <c>TickAsync</c> repeated
/// through detection (8 min) and operator escalation (20 min) on its fake clock. R3 below uses this
/// partial class's <c>BridgeQueueHarness</c>/<c>OpenSweep</c> shapes. Helper names
/// in this file carry the <c>BootStall</c> prefix so they cannot collide with the S2 repair's
/// <c>Brief*</c> partials.
/// </summary>
public partial class DelegationDispatchRecoveryBoundaryTests
{
    /// <summary>
    /// V-10. The shared <see cref="BootStallWorld"/> (A-11): a Working prompt-only Code task whose
    /// brief prompt is nine minutes old, driven by the real <c>TickAsync</c> through detection
    /// (twice), the operator stage at <c>promptAt + 20</c> and a tick from a recreated provider.
    /// inline: the Sent delegation brief row. spilled: the Sent pointer row with its
    /// RemoteSpillBody and the spill file it names. pending-ui-followup: a Pending Ui row queued
    /// behind the Sent brief. The brief carries a persisted delivery identity: the non-null
    /// transcript baseline its attempt captured (0, the floor just before the brief prompt at
    /// sequence 1, the anti-duplicate keystone), the accepted generation it typed into, its start
    /// time and a Delivered verdict. Decisive: every queue row on the delegate session (Id,
    /// Sequence, Status, Origin, DeliveryAttempts, SentAt, ExecutionTaskId,
    /// LastDeliveryBaselineSequence, LastDeliveryGeneration, LastDeliveryStartedAt,
    /// DeliveryVerdict, DeliveryVerdictAt, RemoteSpillRelativePath and the SHA-256 of Body and
    /// RemoteSpillBody) and the spill file's bytes are equal before the first tick and after the
    /// last; the brief still holds the seeded baseline and generation; no new row; runner Inputs 0;
    /// the task keeps its attempt, token and dispatch; both boot stages are written once.
    /// </summary>
    [Test]
    [Arguments("inline")]
    [Arguments("spilled")]
    [Arguments("pending-ui-followup")]
    public async Task C1151_Brief_and_spill_remain_byte_identical(string shape)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = await BootStallWorld.CreateAsync(schema.ConnectionString, new BootStallWorldOptions
        {
            MinutesAgo = 9,
            Brief = QueuedMessageStatus.Sent,
        });
        string? spillPath = null;
        const long baseline = 0;
        var generation = (await world.SessionAsync()).StartedAt;
        try
        {
            await using (var db = world.Read())
            {
                var brief = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == world.SessionId);
                brief.ExecutionTaskId = world.TaskId;
                brief.DeliveryAttempts = 1;
                brief.LastDeliveryBaselineSequence = baseline;
                brief.LastDeliveryGeneration = generation;
                brief.LastDeliveryStartedAt = brief.SentAt.ShouldNotBeNull().AddSeconds(-1);
                brief.DeliveryVerdict = DeliveryVerdict.Delivered;
                brief.DeliveryVerdictAt = brief.SentAt;
                if (shape == "spilled")
                {
                    var stem = brief.Id.ToString("D");
                    var relative = TypedBodySpill.InboxRelativePath(stem);
                    brief.Body = $"{DelegationReportFormatter.TaskMarker(world.TaskId)} Read {relative} in full before you start.";
                    brief.RemoteSpillRelativePath = relative;
                    brief.RemoteSpillBody = $"spill café ☃\nthe whole brief {BootStallWorld.PromptCanary}\n";
                    var cwd = (await world.SessionAsync()).Cwd;
                    spillPath = TypedBodySpill.InboxAbsolutePath(cwd, stem);
                    Directory.CreateDirectory(Path.GetDirectoryName(spillPath)!);
                    await File.WriteAllTextAsync(spillPath, brief.RemoteSpillBody, new UTF8Encoding(false));
                }
                else if (shape == "pending-ui-followup")
                {
                    db.SessionQueuedMessages.Add(new SessionQueuedMessage
                    {
                        Id = Guid.NewGuid(),
                        AgentSessionId = world.SessionId,
                        Sequence = 2,
                        Body = "a follow-up from the UI café ☃",
                        Origin = QueuedMessageOrigin.Ui,
                        Status = QueuedMessageStatus.Pending,
                        CreatedAt = world.Now0.AddMinutes(-1),
                    });
                }
                else if (shape != "inline")
                {
                    throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
                }

                await db.SaveChangesAsync();
            }

            var before = await world.TaskAsync();
            var queue = await BootStallQueueSnapshotAsync(world, spillPath);

            (await world.TickAsync()).SweepFailures.ShouldBe(0, world.Warnings());
            (await world.TickAsync()).SweepFailures.ShouldBe(0, world.Warnings());
            world.Clock.SetUtcNow(new DateTimeOffset(world.PromptAt.AddMinutes(20), TimeSpan.Zero));
            (await world.TickAsync()).SweepFailures.ShouldBe(0, world.Warnings());
            await using (var restarted = world.Recreate())
                (await restarted.TickAsync()).SweepFailures.ShouldBe(0, restarted.Warnings());

            (await world.BootWarningsAsync()).Select(w => w.Split(' ')[0])
                .ShouldBe([BootStallPolicy.DetectedToken, BootStallPolicy.NeedsOperatorToken], world.Warnings());
            (await BootStallQueueSnapshotAsync(world, spillPath))
                .ShouldBe(queue, $"{shape}: detection and escalation leave every queue byte and identity alone");
            await using (var db = world.Read())
            {
                var brief = await db.SessionQueuedMessages.AsNoTracking()
                    .SingleAsync(m => m.AgentSessionId == world.SessionId && m.Origin == QueuedMessageOrigin.Delegation);
                brief.LastDeliveryBaselineSequence.ShouldBe(baseline, $"{shape}: the anti-duplicate baseline survives");
                brief.LastDeliveryGeneration.ShouldBe(generation, shape);
            }

            BootStallWorkingTickCharacterizationTests.AssertNothingDestructive(world);
            await BootStallWorkingTickCharacterizationTests.AssertSameAttemptAsync(world, before);
            await BootStallWorkingTickCharacterizationTests.AssertNoFailureTraceAsync(world);
            (await world.PromptCountAsync()).ShouldBe(1, "nothing was sent to the delegate");
        }
        finally
        {
            if (spillPath is not null && File.Exists(spillPath))
                File.Delete(spillPath);
        }
    }

    /// <summary>Every queue row on the world's session plus the spill file, hashed, one line each.</summary>
    private static async Task<string> BootStallQueueSnapshotAsync(BootStallWorld world, string? spillPath)
    {
        await using var db = world.Read();
        var rows = await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == world.SessionId)
            .OrderBy(m => m.Sequence)
            .ToListAsync();
        var lines = rows.Select(m => string.Join('|',
            m.Id, m.Sequence, m.Status, m.Origin, m.DeliveryAttempts, m.SentAt?.ToString("o"), m.ExecutionTaskId,
            m.LastDeliveryBaselineSequence?.ToString() ?? "-", m.LastDeliveryGeneration?.ToString("o"),
            m.LastDeliveryStartedAt?.ToString("o"), m.DeliveryVerdict, m.DeliveryVerdictAt?.ToString("o"),
            m.RemoteSpillRelativePath, BootStallSha(m.Body), m.RemoteSpillBody is null ? "-" : BootStallSha(m.RemoteSpillBody)))
            .ToList();
        lines.Insert(0, $"rows={rows.Count}");
        if (spillPath is not null)
            lines.Add($"spill={Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(spillPath)))}");
        return string.Join('\n', lines);
    }

    private static string BootStallSha(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

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
