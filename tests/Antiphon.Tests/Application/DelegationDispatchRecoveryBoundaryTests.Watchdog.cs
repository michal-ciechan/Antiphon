using System.Data.Common;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class DelegationDispatchRecoveryBoundaryTests
{
    [Test]
    [Arguments("attach-refused")]
    [Arguments("save-fault")]
    [Arguments("cancel-after-insert")]
    public async Task C1150_Readiness_or_persistence_fault_preserves_custody(string cut)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seeded = await SeedCurrentAsync(schema.ConnectionString, "Do the thing.");
        var adapter = new FakeAgentProtocolAdapter();
        var runner = new RecoveryRunner(seeded.SessionId) { AcceptedStartedAt = seeded.StartedAt };

        if (cut == "attach-refused")
        {
            adapter.ReadyResult = false;
            await using var world = await OpenRecoveryAsync(schema.ConnectionString, adapter, runner, interceptor: null);
            world.Harness.Runtime.SetTestAcceptedStartedAt(seeded.SessionId, seeded.StartedAt);
            await world.ResumeAsync(seeded);

            adapter.Attached.ShouldBeTrue(cut);
            adapter.KillCount.ShouldBe(0, cut);
            runner.Starts.ShouldBe(0, cut);
            runner.Kills.ShouldBe(0, cut);
            await using (var db = world.Read())
            {
                (await db.AgentSessions.SingleAsync(s => s.Id == seeded.SessionId)).Status
                    .ShouldBe(SessionStatus.Running, cut);
                (await db.AgentSessions.SingleAsync(s => s.Id == seeded.SessionId)).LaunchResumedAt
                    .ShouldBeNull(cut);
                (await db.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId)).Status
                    .ShouldBe(AgentTaskStatus.Dispatched, cut);
                (await db.SessionQueuedMessages.CountAsync(m => m.ExecutionTaskId == seeded.TaskId))
                    .ShouldBe(0, cut);
            }

            await AgeDispatchAsync(schema.ConnectionString, seeded.TaskId, 11);
            var sweepRunner = new CountingRunner();
            sweepRunner.Sessions.Add(Listed(
                seeded.SessionId, "Running", seeded.StartedAt, pending: null,
                accepted: seeded.StartedAt.AddMinutes(-30)));
            var stopper = new RecordingSessionStopper();
            await using var host = OpenSweep(
                schema.ConnectionString, sweepRunner, stopper,
                new FakeTimeProvider(DateTimeOffset.UtcNow), new DeadSessionFirstSeenState());
            (await host.NeverStartedAsync()).ShouldBe(0, cut);

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var held = await verify.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
            held.Status.ShouldBe(AgentTaskStatus.Blocked, cut);
            held.CompletedAt.ShouldBeNull(cut);
            held.FailureReason.ShouldBe(AgentTaskDispatcher.DispatchBriefRecoveryHeldReason, cut);
            held.Attempt.ShouldBe(1, cut);
            (await verify.SessionQueuedMessages.CountAsync(m => m.ExecutionTaskId == seeded.TaskId))
                .ShouldBe(0, cut);
            (await verify.AgentTaskEvents.CountAsync(e =>
                e.AgentTaskId == seeded.TaskId && e.Type == AgentTaskEventType.Blocked)).ShouldBe(1, cut);
            (await verify.AgentSessions.SingleAsync(s => s.Id == seeded.SessionId)).Status
                .ShouldBe(SessionStatus.Running, cut);
            Quiet(sweepRunner, stopper, cut);
            return;
        }

        if (cut == "save-fault")
        {
            var fault = new OnceQueuedInsertFault();
            await using var world = await OpenRecoveryAsync(schema.ConnectionString, adapter, runner, fault);
            world.Harness.Runtime.SetTestAcceptedStartedAt(seeded.SessionId, seeded.StartedAt);
            WirePrompt(adapter, world, seeded.SessionId);
            await world.ResumeAsync(seeded);

            adapter.KillCount.ShouldBe(0, cut);
            runner.Kills.ShouldBe(0, cut);
            runner.Starts.ShouldBe(0, cut);
            await using (var mid = world.Read())
            {
                (await mid.SessionQueuedMessages.CountAsync(m => m.ExecutionTaskId == seeded.TaskId))
                    .ShouldBe(0, cut);
                (await mid.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId)).Status
                    .ShouldBe(AgentTaskStatus.Dispatched, cut);
                (await mid.AgentSessions.SingleAsync(s => s.Id == seeded.SessionId)).LaunchResumedAt
                    .ShouldBeNull(cut);
            }

            fault.Armed = false;
            await world.ResumeAsync(seeded);
            await using var verify = world.Read();
            var briefs = await verify.SessionQueuedMessages.Where(m => m.ExecutionTaskId == seeded.TaskId).ToListAsync();
            briefs.Count.ShouldBe(1, cut);
            AssertRecoveredBrief(briefs[0], seeded.Goal, cut);
            var prompts = await verify.TranscriptEntries
                .Where(t => t.AgentSessionId == seeded.SessionId && t.Kind == TranscriptKinds.UserPrompt)
                .Select(t => t.Text ?? "")
                .ToListAsync();
            prompts.Count.ShouldBe(1, cut);
            PromptSubmissionMatch.IsCompleteIn(briefs[0].Body, prompts[0]).ShouldBeTrue(cut);
            runner.Kills.ShouldBe(0, cut);
            adapter.KillCount.ShouldBe(0, cut);
            return;
        }

        using var cts = new CancellationTokenSource();
        var cancel = new CancelAfterBriefCommit(cts);
        Guid briefId;
        await using (var world = await OpenRecoveryAsync(schema.ConnectionString, adapter, runner, cancel))
        {
            world.Harness.Runtime.SetTestAcceptedStartedAt(seeded.SessionId, seeded.StartedAt);
            await Should.ThrowAsync<OperationCanceledException>(() => world.ResumeAsync(seeded, cts.Token));
            adapter.KillCount.ShouldBe(0, cut);
            runner.Kills.ShouldBe(0, cut);
            runner.Starts.ShouldBe(0, cut);
            await using var mid = world.Read();
            var row = (await mid.SessionQueuedMessages.Where(m => m.ExecutionTaskId == seeded.TaskId).ToListAsync())
                .ShouldHaveSingleItem(cut);
            row.Status.ShouldBe(QueuedMessageStatus.Pending, cut);
            row.DeliveryAttempts.ShouldBe(0, cut);
            briefId = row.Id;
            (await mid.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId)).Status
                .ShouldBe(AgentTaskStatus.Dispatched, cut);
            (await mid.AgentSessions.SingleAsync(s => s.Id == seeded.SessionId)).Status
                .ShouldBe(SessionStatus.Running, cut);
            (await mid.AgentSessions.SingleAsync(s => s.Id == seeded.SessionId)).LaunchResumedAt
                .ShouldBeNull(cut);
        }

        var again = new FakeAgentProtocolAdapter();
        var secondRunner = new RecoveryRunner(seeded.SessionId) { AcceptedStartedAt = seeded.StartedAt };
        await using var fresh = await OpenRecoveryAsync(schema.ConnectionString, again, secondRunner, interceptor: null);
        fresh.Harness.Runtime.SetTestAcceptedStartedAt(seeded.SessionId, seeded.StartedAt);
        WirePrompt(again, fresh, seeded.SessionId);
        await fresh.ResumeAsync(seeded);
        await using var delivered = fresh.Read();
        var reused = await delivered.SessionQueuedMessages.Where(m => m.ExecutionTaskId == seeded.TaskId).ToListAsync();
        reused.Count.ShouldBe(1, cut);
        reused[0].Id.ShouldBe(briefId, cut);
        AssertRecoveredBrief(reused[0], seeded.Goal, cut);
        (await delivered.TranscriptEntries.CountAsync(t =>
            t.AgentSessionId == seeded.SessionId && t.Kind == TranscriptKinds.UserPrompt)).ShouldBe(1, cut);
        again.KillCount.ShouldBe(0, cut);
        secondRunner.Kills.ShouldBe(0, cut);
        secondRunner.Starts.ShouldBe(0, cut);
    }

    [Test]
    public async Task C1150_Watchdog_first_recovers_before_failure()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seeded = await SeedCurrentAsync(schema.ConnectionString, "Do the thing.");
        await AgeDispatchAsync(schema.ConnectionString, seeded.TaskId, 11);
        var adapter = new FakeAgentProtocolAdapter();
        var runner = new RecoveryRunner(seeded.SessionId) { AcceptedStartedAt = seeded.StartedAt };
        await using var world = await OpenDispatchAsync(schema.ConnectionString, seeded, adapter, runner);
        WireDispatchPrompt(adapter, world, seeded.SessionId);

        await using (var scope = world.Harness.Provider.CreateAsyncScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
            (await dispatcher.FailNeverStartedAsync(CancellationToken.None)).ShouldBe(0, "V-15");
        }

        await using var verify = world.Read();
        var task = await verify.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched, "V-15");
        task.Attempt.ShouldBe(1, "V-15");
        task.FailureReason.ShouldBeNull("V-15");
        var session = await verify.AgentSessions.SingleAsync(s => s.Id == seeded.SessionId);
        session.Status.ShouldBe(SessionStatus.Running, "V-15");
        session.LaunchResumedAt.ShouldBeNull("V-15");
        var briefs = await verify.SessionQueuedMessages.Where(m => m.ExecutionTaskId == seeded.TaskId).ToListAsync();
        briefs.Count.ShouldBe(1, "V-15");
        AssertRecoveredBrief(briefs[0], seeded.Goal, "V-15");
        var prompts = await verify.TranscriptEntries
            .Where(t => t.AgentSessionId == seeded.SessionId && t.Kind == TranscriptKinds.UserPrompt)
            .Select(t => t.Text ?? "")
            .ToListAsync();
        prompts.Count.ShouldBe(1, "V-15");
        prompts[0].Contains(DelegationReportFormatter.TaskMarker(seeded.TaskId), StringComparison.Ordinal)
            .ShouldBeTrue("V-15");
        PromptSubmissionMatch.IsCompleteIn(briefs[0].Body, prompts[0]).ShouldBeTrue("V-15");
        runner.Starts.ShouldBe(0, "V-15");
        runner.Kills.ShouldBe(0, "V-15");
        adapter.KillCount.ShouldBe(0, "V-15");
        adapter.StartedAcceptedGeneration.ShouldBeNull("V-15");
        world.Stopper.Killed.ShouldBeEmpty("V-15");
    }

    [Test]
    public async Task C1149_Watchdog_first_holds_absent_launch()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seeded = await SeedAsync(schema.ConnectionString, new AbsentShape { Parent = true, AgeMinutes = 11 });
        var runner = new CountingRunner();
        var stopper = new RecordingSessionStopper();
        await using var host = OpenSweep(
            schema.ConnectionString, runner, stopper,
            new FakeTimeProvider(DateTimeOffset.UtcNow), new DeadSessionFirstSeenState());
        (await host.NeverStartedAsync()).ShouldBe(0, "V-16");

        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var held = await verify.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
        held.Status.ShouldBe(AgentTaskStatus.Blocked, "V-16");
        held.CompletedAt.ShouldBeNull("V-16");
        held.FailureReason.ShouldBe(AgentTaskDispatcher.DispatchLaunchAbsentReason, "V-16");
        held.Attempt.ShouldBe(seeded.Attempt, "V-16");
        held.Goal.ShouldBe(seeded.Goal, "V-16");
        held.DispatchedAt.ShouldBe(seeded.DispatchedAt, "V-16");
        var blocked = await verify.AgentTaskEvents
            .Where(e => e.AgentTaskId == seeded.TaskId && e.Type == AgentTaskEventType.Blocked)
            .ToListAsync();
        blocked.Count.ShouldBe(1, "V-16");
        blocked[0].Detail.ShouldBe(AgentTaskDispatcher.DispatchLaunchAbsentReason, "V-16");
        var brief = (await verify.SessionQueuedMessages.Where(m => m.ExecutionTaskId == seeded.TaskId).ToListAsync())
            .ShouldHaveSingleItem("V-16");
        brief.Id.ShouldBe(seeded.BriefId, "V-16");
        brief.Status.ShouldBe(QueuedMessageStatus.Pending, "V-16");
        brief.DeliveryAttempts.ShouldBe(0, "V-16");
        Encoding.UTF8.GetBytes(brief.Body).ShouldBe(seeded.Body, "V-16");
        (brief.RemoteSpillBody is null ? null : Encoding.UTF8.GetBytes(brief.RemoteSpillBody))
            .ShouldBe(seeded.Spill, "V-16");
        var note = (await verify.SessionQueuedMessages.Where(m => m.AgentSessionId == seeded.ParentId).ToListAsync())
            .ShouldHaveSingleItem("V-16");
        note.SourceTaskId.ShouldBe(seeded.TaskId, "V-16");
        note.ExecutionTaskId.ShouldBeNull("V-16");
        Quiet(runner, stopper, "V-16");
    }

    [Test]
    public async Task C1150_Recovery_exhaustion_is_visible_without_replacement()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var taskId = Guid.NewGuid();
        var marker = DelegationReportFormatter.TaskMarker(taskId);
        var seeded = await SeedAsync(schema.ConnectionString, new AbsentShape
        {
            TaskId = taskId,
            LiveStatus = SessionStatus.Running,
            AgeMinutes = 11,
            DeliveryAttempts = 3,
            Body = marker + "\n\nDo the thing.",
            Spill = null,
        });
        var runner = new CountingRunner();
        runner.Sessions.Add(Listed(
            seeded.SessionId, "Running", seeded.StartedAt, pending: null,
            accepted: seeded.StartedAt.AddMinutes(-30)));
        var stopper = new RecordingSessionStopper();
        await using (var host = OpenSweep(
            schema.ConnectionString, runner, stopper,
            new FakeTimeProvider(DateTimeOffset.UtcNow), new DeadSessionFirstSeenState()))
        {
            (await host.NeverStartedAsync()).ShouldBe(0, "V-17");
        }

        await using (var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var held = await verify.AgentTasks.SingleAsync(t => t.Id == taskId);
            held.Status.ShouldBe(AgentTaskStatus.Blocked, "V-17");
            held.CompletedAt.ShouldBeNull("V-17");
            held.FailureReason.ShouldBe(AgentTaskDispatcher.DispatchBriefRecoveryHeldReason, "V-17");
            held.Attempt.ShouldBe(1, "V-17");
            var row = (await verify.SessionQueuedMessages.Where(m => m.ExecutionTaskId == taskId).ToListAsync())
                .ShouldHaveSingleItem("V-17");
            row.Id.ShouldBe(seeded.BriefId, "V-17");
            row.DeliveryAttempts.ShouldBe(3, "V-17");
            row.Status.ShouldBe(QueuedMessageStatus.Pending, "V-17");
            row.Body.ShouldBe(marker + "\n\nDo the thing.", "V-17");
            (await verify.AgentTaskEvents.CountAsync(e =>
                e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Blocked)).ShouldBe(1, "V-17");
            (await verify.AgentSessions.SingleAsync(s => s.Id == seeded.SessionId)).Status
                .ShouldBe(SessionStatus.Running, "V-17");
        }

        var again = new CountingRunner();
        again.Sessions.Add(Listed(
            seeded.SessionId, "Running", seeded.StartedAt, pending: null,
            accepted: seeded.StartedAt.AddMinutes(-30)));
        var secondStop = new RecordingSessionStopper();
        await using var restarted = OpenSweep(
            schema.ConnectionString, again, secondStop,
            new FakeTimeProvider(DateTimeOffset.UtcNow), new DeadSessionFirstSeenState());
        (await restarted.NeverStartedAsync()).ShouldBe(0, "V-17");

        await using var after = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        (await after.AgentTaskEvents.CountAsync(e =>
            e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Blocked)).ShouldBe(1, "V-17");
        (await after.SessionQueuedMessages.CountAsync(m => m.ExecutionTaskId == taskId)).ShouldBe(1, "V-17");
        (await after.SessionQueuedMessages.SingleAsync(m => m.Id == seeded.BriefId)).DeliveryAttempts
            .ShouldBe(3, "V-17");
        Quiet(runner, stopper, "V-17");
        Quiet(again, secondStop, "V-17");
    }

    [Test]
    [Arguments("working-before")]
    [Arguments("working-after-catch-up")]
    [Arguments("listed-session")]
    [Arguments("unavailable-inventory")]
    [Arguments("failure-write-loses")]
    public async Task C1150_Watchdog_cleanup_requires_failure_and_fresh_safe_evidence(string gate)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var taskId = Guid.NewGuid();
        var marker = DelegationReportFormatter.TaskMarker(taskId);
        var working = gate is "working-before" or "working-after-catch-up";
        var seeded = await SeedAsync(schema.ConnectionString, new AbsentShape
        {
            TaskId = taskId,
            LiveStatus = SessionStatus.Running,
            AgeMinutes = 11,
            Brief = gate is not ("working-after-catch-up" or "failure-write-loses"),
            DeliveryAttempts = gate is "listed-session" or "unavailable-inventory" ? 1 : 0,
            Body = marker + "\n\nDo the thing.",
            Spill = null,
        });
        if (gate == "working-before")
            await InsertAssistantAsync(schema.ConnectionString, seeded.SessionId, seeded.DispatchedAt.AddMinutes(1));

        var runner = new CountingRunner();
        if (gate == "listed-session")
        {
            runner.Sessions.Add(Listed(
                seeded.SessionId, "Running", seeded.StartedAt, pending: null, accepted: null));
        }
        else if (gate == "unavailable-inventory")
        {
            runner.ThrowOnList = new IOException("owning inventory unavailable");
        }

        var stopper = new RecordingSessionStopper();
        await using var host = OpenSweep(
            schema.ConnectionString, runner, stopper,
            new FakeTimeProvider(DateTimeOffset.UtcNow), new DeadSessionFirstSeenState());
        var failed = await host.NeverStartedAsync(dispatcher =>
        {
            if (gate == "working-after-catch-up")
            {
                dispatcher.CatchUpOverride = async (sessionId, ct) =>
                    await InsertAssistantAsync(schema.ConnectionString, sessionId, DateTime.UtcNow);
            }
            else if (gate == "failure-write-loses")
            {
                dispatcher.BeforeNeverStartedCleanupAsync = async (task, _, ct) =>
                {
                    await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
                    await db.AgentTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(
                        s => s.SetProperty(t => t.Status, AgentTaskStatus.Dispatched)
                            .SetProperty(t => t.FailureReason, (string?)null), ct);
                };
            }
        });

        // Listed and unavailable inventory still take today's failure. The stop is what those
        // gates withhold. Working and a failure write that loses to settlement never fail.
        failed.ShouldBe(gate is "listed-session" or "unavailable-inventory" ? 1 : 0, gate);
        runner.Kills.ShouldBe(0, gate);
        runner.Releases.ShouldBe(0, gate);
        runner.Starts.ShouldBe(0, gate);
        stopper.Killed.ShouldBeEmpty(gate);
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await verify.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Attempt.ShouldBe(1, gate);
        if (working || gate == "failure-write-loses")
            task.Status.ShouldBe(AgentTaskStatus.Dispatched, gate);
        else
        {
            task.Status.ShouldBe(AgentTaskStatus.Failed, gate);
            task.FailureReason.ShouldNotBeNull(gate);
            task.FailureReason.Contains("dispatch_brief_recovery_held", StringComparison.Ordinal)
                .ShouldBeFalse(gate);
            task.FailureReason.Contains("dispatch_launch_absent", StringComparison.Ordinal)
                .ShouldBeFalse(gate);
        }

        await AssertUnrelatedFailureStillStopsAsync();
    }

    [Test]
    [Arguments("active-working")]
    [Arguments("aged-prompt-only")]
    public async Task C1149_C1150_Working_full_tick_safety(string shape)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        if (shape == "aged-prompt-only")
        {
            await using var world = await BootStallWorld.CreateAsync(
                schema.ConnectionString, new BootStallWorldOptions { MinutesAgo = 9, MaxConcurrentTasks = 0 });
            var before = await world.TaskAsync();
            var tick = await world.TickAsync();
            tick.SweepFailures.ShouldBe(0, world.Warnings());
            tick.Dispatched.ShouldBe(0, shape);
            BootStallWorkingTickCharacterizationTests.AssertNothingDestructive(world);
            await BootStallWorkingTickCharacterizationTests.AssertSameAttemptAsync(world, before);
            var warnings = await world.BootWarningsAsync();
            warnings.Count.ShouldBe(1, shape);
            warnings[0].StartsWith(BootStallPolicy.DetectedToken + " ", StringComparison.Ordinal)
                .ShouldBeTrue(shape);
            (await world.PromptCountAsync()).ShouldBe(1, shape);
            return;
        }

        var seeded = await SeedCurrentAsync(schema.ConnectionString, "Do the thing.");
        await InsertAssistantAsync(schema.ConnectionString, seeded.SessionId, seeded.DispatchedAt.AddMinutes(1));
        var adapter = new FakeAgentProtocolAdapter();
        var runner = new RecoveryRunner(seeded.SessionId) { AcceptedStartedAt = seeded.StartedAt };
        await using var dispatch = await OpenDispatchAsync(schema.ConnectionString, seeded, adapter, runner);
        await DiscoverDispatchAsync(dispatch);
        await using (var scope = dispatch.Harness.Provider.CreateAsyncScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
            dispatcher.WorkspaceProbeOverride = new StubWorkspaceProgressProbe(
                new WorkspaceProgressArm(true, null, null, false));
            var tick = await dispatcher.TickAsync(CancellationToken.None);
            tick.SweepFailures.ShouldBe(0, shape);
            tick.Dispatched.ShouldBe(0, shape);
        }

        adapter.Attached.ShouldBeFalse(shape);
        adapter.KillCount.ShouldBe(0, shape);
        adapter.StartedAcceptedGeneration.ShouldBeNull(shape);
        runner.Starts.ShouldBe(0, shape);
        runner.Kills.ShouldBe(0, shape);
        dispatch.Stopper.Killed.ShouldBeEmpty(shape);
        await using var verify = dispatch.Read();
        var task = await verify.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched, shape);
        task.Attempt.ShouldBe(1, shape);
        (await verify.AgentSessions.SingleAsync(s => s.Id == seeded.SessionId)).LaunchResumedAt
            .ShouldBeNull(shape);
        (await verify.SessionQueuedMessages.CountAsync(m => m.ExecutionTaskId == seeded.TaskId))
            .ShouldBe(0, shape);
        (await verify.TranscriptEntries.CountAsync(t =>
            t.AgentSessionId == seeded.SessionId && t.Kind == TranscriptKinds.UserPrompt))
            .ShouldBe(0, shape);
    }

    private static async Task AssertUnrelatedFailureStillStopsAsync()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seeded = await SeedAsync(schema.ConnectionString, new AbsentShape
        {
            LiveStatus = SessionStatus.Running,
            AgeMinutes = 11,
            Brief = false,
            SessionReason = null,
        });
        var runner = new CountingRunner();
        var stopper = new RecordingSessionStopper();
        await using var host = OpenSweep(
            schema.ConnectionString, runner, stopper,
            new FakeTimeProvider(DateTimeOffset.UtcNow), new DeadSessionFirstSeenState());
        (await host.NeverStartedAsync()).ShouldBe(1, "unrelated");
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await verify.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
        task.Status.ShouldBe(AgentTaskStatus.Failed, "unrelated");
        task.FailureReason.ShouldNotBeNull("unrelated");
        task.FailureReason.Contains("Boot prompt was never delivered", StringComparison.Ordinal)
            .ShouldBeTrue("unrelated");
        stopper.Killed.ShouldContain(seeded.SessionId, "unrelated");
        runner.Releases.ShouldBe(0, "unrelated");
        runner.Starts.ShouldBe(0, "unrelated");
    }

    private static async Task AgeDispatchAsync(string connection, Guid taskId, int minutes)
    {
        var due = Pg(DateTime.UtcNow.AddMinutes(-minutes));
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        await db.AgentTasks.Where(t => t.Id == taskId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.DispatchedAt, due));
    }

    private static async Task InsertAssistantAsync(string connection, Guid sessionId, DateTime at)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var sequence = ((await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence)) ?? 0) + 1;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = sequence,
            Kind = TranscriptKinds.AssistantText,
            Text = "still working on the turn",
            Timestamp = at,
            CreatedAt = at,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<DispatchWorld> OpenDispatchAsync(
        string connection,
        CurrentBrief seeded,
        FakeAgentProtocolAdapter adapter,
        RecoveryRunner runner,
        IInterceptor? interceptor = null)
    {
        var stopper = new RecordingSessionStopper();
        var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = connection,
            PreserveDatabaseOnDispose = true,
            ConfigureDbContext = interceptor is null ? null : options => options.AddInterceptors(interceptor),
            ConfigureServices = services =>
            {
                services.AddSingleton<IAgentProtocolAdapterFactory>(new RecoveryAdapterFactory(adapter));
                services.AddSingleton<ISessionRunnerClient>(runner);
                services.AddSingleton<DeadSessionFirstSeenState>();
                services.AddSingleton<IDelegateSessionStopper>(stopper);
                services.AddSingleton<DelegationWorkspaceResolver>();
                services.AddDelegationWorktreeGraph();
                services.AddScoped<AgentTaskService>();
                services.AddScoped<AgentTaskDispatcher>();
            },
        });
        adapter.RegisterOnStart = harness.Runtime;
        harness.Runtime.SetTestAcceptedStartedAt(seeded.SessionId, seeded.StartedAt);
        return new DispatchWorld(harness, stopper, runner, adapter);
    }

    private static async Task DiscoverDispatchAsync(DispatchWorld world)
    {
        var queue = world.Harness.Provider.GetRequiredService<AgentSessionLaunchQueue>();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(world.Harness.ConnectionString));
        var service = SessionReconciliationServiceTests.BuildService(
            db,
            world.Harness.Provider.GetRequiredService<ISessionRunnerClient>(),
            world.Harness.EventBus,
            ownership: queue);
        await service.ScanAsync(CancellationToken.None);
        await queue.WaitForIdleAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
    }

    private static void WireDispatchPrompt(FakeAgentProtocolAdapter adapter, DispatchWorld world, Guid sessionId)
    {
        var sequence = 0;
        adapter.OnSubmitted = async body =>
        {
            var promptSequence = Interlocked.Add(ref sequence, 2) - 1;
            var now = DateTime.UtcNow;
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(world.Harness.ConnectionString));
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = promptSequence,
                Kind = TranscriptKinds.UserPrompt,
                Uuid = $"c1150-{Guid.NewGuid():N}",
                Role = "user",
                Text = body,
                Timestamp = now,
                CreatedAt = now,
            });
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = promptSequence + 1,
                Kind = TranscriptKinds.TurnEnd,
                Uuid = $"c1150-end-{Guid.NewGuid():N}",
                Timestamp = now,
                CreatedAt = now,
                StopReason = "end_turn",
            });
            await db.SaveChangesAsync();
        };
    }

    private sealed record DispatchWorld(
        BridgeQueueHarness Harness,
        RecordingSessionStopper Stopper,
        RecoveryRunner Runner,
        FakeAgentProtocolAdapter Adapter) : IAsyncDisposable
    {
        public AppDbContext Read() => new(TestDbFixture.CreateDbContextOptions(Harness.ConnectionString));

        public ValueTask DisposeAsync() => Harness.DisposeAsync();
    }

    private sealed class CancelAfterBriefCommit(CancellationTokenSource cancel) : DbCommandInterceptor
    {
        private bool _inserted;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            CancelOnCommandAfterInsert(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            CancelOnCommandAfterInsert(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            CancelOnCommandAfterInsert(command);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            var executed = await base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
            NoteInsert(command);
            // Npgsql's COMMIT is usually a protocol message, not this interceptor. When it is a
            // short command, it has already succeeded by Executed, so cancelling here is after durability.
            CancelIfCommit(command);
            return executed;
        }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            var executed = await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
            NoteInsert(command);
            CancelIfCommit(command);
            return executed;
        }

        private void NoteInsert(DbCommand command)
        {
            if (IsBriefInsert(command))
                _inserted = true;
        }

        private static bool IsBriefInsert(DbCommand command) =>
            command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase)
            && command.CommandText.Contains("SessionQueuedMessages", StringComparison.OrdinalIgnoreCase);

        private void CancelOnCommandAfterInsert(DbCommand command)
        {
            // The insert's own Executing runs before NoteInsert. The next command is the
            // post-commit read: the transaction has committed, and this token must surface.
            if (_inserted && !IsBriefInsert(command))
                CancelObserved();
        }

        private void CancelIfCommit(DbCommand command)
        {
            var text = command.CommandText.Trim();
            if (_inserted
                && text.Length <= 20
                && text.Contains("COMMIT", StringComparison.OrdinalIgnoreCase)
                && !text.Contains("ROLLBACK", StringComparison.OrdinalIgnoreCase))
                CancelObserved();
        }

        private void CancelObserved()
        {
            cancel.Cancel();
            throw new OperationCanceledException(cancel.Token);
        }
    }
}
