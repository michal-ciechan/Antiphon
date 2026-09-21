using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.TestHelpers.WallRerouteFixture;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class WallRerouteDispatchTests
{
    [Test]
    public async Task Grok_wall_reroute_to_Claude_dispatches_on_the_next_tick() =>
        await AssertWallDispatchAsync(AgentKind.Grok,
            "Grok Build usage balance exhausted [after 1 retries]");

    [Test]
    public async Task Same_kind_wall_reroute_fable_to_opus_dispatches_on_the_next_tick() =>
        await AssertWallDispatchAsync(AgentKind.ClaudeCode, UsageLimitWallParser.FableModelCapIncidentText);

    private static async Task AssertWallDispatchAsync(AgentKind walledKind, string wallText)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        using var harness = new WallRerouteHarness(schema.ConnectionString, workspace.Path);
        await SeedChainAsync(schema, (walledKind, AgentModelLevel.Frontier),
            (AgentKind.ClaudeCode, AgentModelLevel.High));
        var (task, sessionId, _) = await SeedWorkingChainTaskAsync(schema, workspace.Path);
        await using (var db = CreateContext(schema))
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
            row.AgentKind = walledKind;
            var session = await db.AgentSessions.SingleAsync(s => s.Id == sessionId);
            session.AgentKind = walledKind;
            session.EffectiveModelId = walledKind == AgentKind.Grok ? "grok-4.7" : "fable";
            await db.SaveChangesAsync();
        }
        await SeedApiErrorStubTurnAsync(schema, sessionId, task.Id, wallText);
        await harness.Reply.OnTurnEndAsync(sessionId, CancellationToken.None);
        await using (var afterReply = CreateContext(schema))
        {
            var wait = await afterReply.CapacityRecoveryWaits.SingleAsync(w => w.TaskId == task.Id);
            wait.ConsumerKind.ShouldBe(CapacityWaitConsumerKind.LiveSession);
            wait.ExecutionKind.ShouldBe(walledKind);
            (await afterReply.AgentTasks.SingleAsync(t => t.Id == task.Id)).Status.ShouldBe(AgentTaskStatus.Queued);
        }
        var (agentId, _) = await SeedWarmOpusAsync(schema, workspace.Path);
        var result = await CapacityRecoveryTaskTests.CreateDispatcher(schema.ConnectionString).TickAsync(CancellationToken.None);
        result.Dispatched.ShouldBe(1);
        result.Failures.ShouldBe(0);
        result.SkippedCapacityWait.ShouldBe(0);
        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched);
        stored.AgentId.ShouldBe(agentId);
        stored.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        stored.ModelLevel.ShouldBe(AgentModelLevel.High);
        var ended = await verify.CapacityRecoveryWaits.SingleAsync(w => w.TaskId == task.Id);
        ended.State.ShouldBe(CapacityRecoveryWaitState.Superseded);
        ended.OutcomeReason.ShouldBe("requeued:Rerouted:attempt-2");
    }

    [Test]
    public async Task Gate_ignores_an_unfinished_wait_on_another_kind()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var task = await SeedQueuedOpusAsync(schema, workspace.Path);
        var wait = await SeedWaitAsync(schema, task.Id, AgentKind.Grok, "grok-4.7", liveSession: true);
        var result = await CapacityRecoveryTaskTests.CreateDispatcher(schema.ConnectionString).TickAsync(CancellationToken.None);
        result.Dispatched.ShouldBe(1);
        await using var verify = CreateContext(schema);
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
        var untouched = await verify.CapacityRecoveryWaits.SingleAsync(w => w.Id == wait.Id);
        untouched.State.ShouldBe(wait.State);
        untouched.Version.ShouldBe(wait.Version);
    }

    [Test]
    public async Task Dispatch_time_rewalk_supersedes_the_old_alias_wait()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        await SeedHardChainAsync(schema);
        var task = await SeedQueuedOpusAsync(schema, workspace.Path);
        await using (var db = CreateContext(schema))
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
            row.Complexity = TaskComplexity.Hard;
            row.ModelLevel = AgentModelLevel.Frontier;
            await db.SaveChangesAsync();
        }
        await SeedHoldAsync(schema, AgentKind.ClaudeCode, "fable");
        var wait = await SeedWaitAsync(schema, task.Id, AgentKind.ClaudeCode, "fable", ready: false);
        var result = await CapacityRecoveryTaskTests.CreateDispatcher(schema.ConnectionString).TickAsync(CancellationToken.None);
        result.Dispatched.ShouldBe(1);
        await using var verify = CreateContext(schema);
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
        var ended = await verify.CapacityRecoveryWaits.SingleAsync(w => w.Id == wait.Id);
        ended.State.ShouldBe(CapacityRecoveryWaitState.Superseded);
        ended.OutcomeReason.ShouldBe("rerouted-at-dispatch");
        var reroute = await verify.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Rerouted);
        reroute.Detail.ShouldContain("fable held → opus");
        reroute.Detail.ShouldContain("at dispatch");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Routing_blocked_resume_registers_the_chosen_candidate(bool alreadyChosen)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        await SeedChainAsync(schema, (AgentKind.ClaudeCode, AgentModelLevel.High));
        var task = await SeedQueuedOpusAsync(schema, workspace.Path);
        await using (var db = CreateContext(schema))
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
            row.Status = AgentTaskStatus.Blocked;
            row.Complexity = TaskComplexity.Hard;
            row.AgentKind = AgentKind.Grok;
            row.FailureReason = ComplexityRoutingService.RoutingExhaustedPrefix + " test";
            await db.SaveChangesAsync();
        }
        var wait = await SeedWaitAsync(schema, task.Id,
            alreadyChosen ? AgentKind.ClaudeCode : AgentKind.Grok, alreadyChosen ? "opus" : "grok-4.7");
        var result = await CapacityRecoveryTaskTests.CreateDispatcher(schema.ConnectionString).TickAsync(CancellationToken.None);
        result.ResumedRoutingBlocked.ShouldBe(1);
        await using var verify = CreateContext(schema);
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).Status.ShouldBe(AgentTaskStatus.Queued);
        var waits = await verify.CapacityRecoveryWaits.Where(w => w.TaskId == task.Id).ToListAsync();
        var current = waits.Single(w => w.State != CapacityRecoveryWaitState.Superseded);
        current.ExecutionKind.ShouldBe(AgentKind.ClaudeCode);
        current.RequestedAlias.ShouldBe("opus");
        if (alreadyChosen)
        {
            waits.Count.ShouldBe(1);
            current.Id.ShouldBe(wait.Id);
        }
        else
        {
            waits.Count.ShouldBe(2);
            var old = waits.Single(w => w.Id == wait.Id);
            old.State.ShouldBe(CapacityRecoveryWaitState.Superseded);
            old.OutcomeReason.ShouldBe("resumed-routing-blocked");
            current.ConsumerKind.ShouldBe(CapacityWaitConsumerKind.RoutingBlockedTask);
        }
    }

    [Test]
    public async Task Cancel_supersedes_the_task_wait()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var task = await SeedQueuedOpusAsync(schema, workspace.Path);
        var wait = await SeedWaitAsync(schema, task.Id, AgentKind.ClaudeCode, "opus");
        var (capacity, _, provider) = CapacityRecoveryTestSupport.CreateService(schema);
        await using var services = provider;
        (await capacity.GrantReadyAsync(CancellationToken.None)).ShouldBe(1);
        using var harness = new WallRerouteHarness(schema.ConnectionString, workspace.Path);
        using var scope = harness.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CancelAsync(task.Id, CancellationToken.None);
        await using var verify = CreateContext(schema);
        var ended = await verify.CapacityRecoveryWaits.SingleAsync(w => w.Id == wait.Id);
        ended.State.ShouldBe(CapacityRecoveryWaitState.Superseded);
        ended.OutcomeReason.ShouldBe("task-canceled");
        (await verify.CapacityRecoveryProviderStates.SingleAsync(s => s.Kind == AgentKind.ClaudeCode)).GrantedWaitId.ShouldBeNull();
    }

    [Test]
    public async Task Gate_skip_writes_one_Held_event_and_counts() => await AssertGateTraceAsync(priorLease: false);

    [Test]
    public async Task Prior_lease_hold_does_not_mute_the_capacity_trace() => await AssertGateTraceAsync(priorLease: true);

    private static async Task AssertGateTraceAsync(bool priorLease)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var task = await SeedQueuedOpusAsync(schema, workspace.Path);
        var wait = await SeedWaitAsync(schema, task.Id, AgentKind.ClaudeCode, "opus");
        if (priorLease)
        {
            await using var db = CreateContext(schema);
            for (var i = 0; i < 3; i++)
                db.AgentTaskEvents.Add(new AgentTaskEvent
                {
                    Id = Guid.NewGuid(), AgentTaskId = task.Id, Type = AgentTaskEventType.Held,
                    Detail = "Held: repository mutation lease is occupied or unavailable.",
                    At = DateTime.UtcNow.AddMinutes(-3 + i),
                });
            await db.SaveChangesAsync();
        }
        var dispatcher = CapacityRecoveryTaskTests.CreateDispatcher(schema.ConnectionString);
        for (var tick = 0; tick < (priorLease ? 4 : 3); tick++)
        {
            var result = await dispatcher.TickAsync(CancellationToken.None);
            result.SkippedCapacityWait.ShouldBe(1);
            result.Dispatched.ShouldBe(0);
        }
        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Queued);
        stored.CapacityWaitId.ShouldBe(wait.Id);
        stored.CapacityWaitRetained.ShouldBeFalse();
        stored.CapacityWaitReason.ShouldBeNull();
        var held = await verify.AgentTaskEvents.Where(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Held).ToListAsync();
        held.Count.ShouldBe(priorLease ? 4 : 1);
        held.Single(e => e.Detail.Contains("waiting for ClaudeCode capacity"))
            .Detail.ShouldContain(DelegationReportFormatter.Short(wait.Id));
    }

    [Test]
    public async Task Scope_hold_still_traces_once()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var (holder, _, _) = await SeedWorkingChainTaskAsync(schema, workspace.Path, complexity: null);
        var task = await SeedQueuedOpusAsync(schema, workspace.Path);
        await using (var db = CreateContext(schema))
        {
            foreach (var row in await db.AgentTasks.Where(t => t.Id == holder.Id || t.Id == task.Id).ToListAsync())
                row.Scope = "server/Application/Services";
            await db.SaveChangesAsync();
        }
        var dispatcher = CapacityRecoveryTaskTests.CreateDispatcher(schema.ConnectionString);
        for (var tick = 0; tick < 2; tick++)
        {
            var result = await dispatcher.TickAsync(CancellationToken.None);
            result.SkippedScope.ShouldBe(1);
            result.Dispatched.ShouldBe(0);
        }
        await using var verify = CreateContext(schema);
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Held)).ShouldBe(1);
    }

    [Test]
    public async Task Repeated_model_hold_persists_the_new_wait_without_duplicate_trace()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var task = await SeedQueuedOpusAsync(schema, workspace.Path);
        await SeedHoldAsync(schema, AgentKind.ClaudeCode, "opus");
        await using (var db = CreateContext(schema))
        {
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(), AgentTaskId = task.Id, Type = AgentTaskEventType.Held,
                Detail = "opus is held; dispatch paused for that model.", At = DateTime.UtcNow.AddMinutes(-1),
            });
            await db.SaveChangesAsync();
        }
        var result = await CapacityRecoveryTaskTests.CreateDispatcher(schema.ConnectionString).TickAsync(CancellationToken.None);
        result.SkippedModelAvailability.ShouldBe(1);
        await using var verify = CreateContext(schema);
        (await verify.CapacityRecoveryWaits.SingleAsync(w => w.TaskId == task.Id)).State.ShouldBe(CapacityRecoveryWaitState.WaitingForHold);
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Held)).ShouldBe(1);
    }

    [Test]
    public async Task Receipt_throw_after_launch_leaves_the_task_dispatched()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var task = await SeedQueuedOpusAsync(schema, workspace.Path);
        var (service, _, provider) = CapacityRecoveryTestSupport.CreateService(schema);
        await using var services = provider;
        var wait = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"task:{task.Id:N}", CapacityWaitConsumerKind.QueuedTask, taskId: task.Id,
            holdAlreadyCleared: true), CancellationToken.None);
        (await service.GrantReadyAsync(CancellationToken.None)).ShouldBe(1);
        var fault = new ThrowOnReceiptSave();
        var result = await CapacityRecoveryTaskTests.CreateDispatcher(schema.ConnectionString, fault)
            .TickAsync(CancellationToken.None);
        fault.Throws.ShouldBe(1);
        result.Dispatched.ShouldBe(1);
        result.Failures.ShouldBe(0);
        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched);
        stored.AgentSessionId.ShouldNotBeNull();
        stored.FailureReason.ShouldBeNull();
        var leftover = await verify.CapacityRecoveryWaits.SingleAsync(w => w.Id == wait.Id);
        leftover.State.ShouldBe(CapacityRecoveryWaitState.Admitted);
        leftover.SessionId.ShouldBeNull();
        leftover.LaunchSessionId.ShouldBeNull();
    }

    [Test]
    public async Task Failed_receipt_cannot_resurrect_a_concurrently_superseded_wait()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var task = await SeedQueuedOpusAsync(schema, workspace.Path);
        var (service, _, provider) = CapacityRecoveryTestSupport.CreateService(schema);
        await using var services = provider;
        var wait = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"task:{task.Id:N}", CapacityWaitConsumerKind.QueuedTask, taskId: task.Id,
            holdAlreadyCleared: true), CancellationToken.None);
        (await service.GrantReadyAsync(CancellationToken.None)).ShouldBe(1);
        var fault = new ThrowOnReceiptSave();
        var result = await CapacityRecoveryTaskTests.CreateDispatcher(schema.ConnectionString, fault)
            .TickAsync(CancellationToken.None);
        fault.Throws.ShouldBe(1);
        result.Dispatched.ShouldBe(1);
        result.Failures.ShouldBe(0);
        var dispatcherDb = fault.Context!;
        dispatcherDb.Database.CurrentTransaction.ShouldBeNull();

        await using var concurrent = CreateContext(schema);
        (await service.SupersedeTaskWaitsOnAsync(concurrent, task.Id, "concurrent-supersede",
            CancellationToken.None)).ShouldBe(1);
        var ended = await concurrent.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == wait.Id);
        ended.State.ShouldBe(CapacityRecoveryWaitState.Superseded);

        // Same scoped dispatcher context, after the failed receipt transaction released
        // its lock: the post-dispatch warning path also saves unrelated task events here.
        var warningId = Guid.NewGuid();
        dispatcherDb.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = warningId, AgentTaskId = task.Id, Type = AgentTaskEventType.Warning,
            At = DateTime.UtcNow, Detail = "subsequent dispatcher warning",
        });
        await dispatcherDb.SaveChangesAsync();
        var stored = await concurrent.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == wait.Id);
        stored.State.ShouldBe(CapacityRecoveryWaitState.Superseded);
        stored.Outcome.ShouldBe(ended.Outcome);
        stored.OutcomeReason.ShouldBe(ended.OutcomeReason);
        stored.Version.ShouldBe(ended.Version);
        stored.SessionId.ShouldBeNull();
        stored.LaunchSessionId.ShouldBeNull();
        stored.DispatchAttemptId.ShouldBeNull();
        (await concurrent.AgentTaskEvents.CountAsync(e => e.Id == warningId)).ShouldBe(1);
        (await concurrent.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Dispatched);
        fault.Throws.ShouldBe(1, "the receipt fault must be single-use, including subsequent saves");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Redeemed_dispatch_receipts_the_wait_and_the_transcript_progresses_it(bool withWait)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var task = await SeedQueuedOpusAsync(schema, workspace.Path);
        var (service, time, provider) = CapacityRecoveryTestSupport.CreateService(schema);
        await using var services = provider;
        var wait = withWait ? await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"task:{task.Id:N}", CapacityWaitConsumerKind.QueuedTask, taskId: task.Id,
            holdAlreadyCleared: true), CancellationToken.None) : null;
        if (withWait)
            (await service.GrantReadyAsync(CancellationToken.None)).ShouldBe(1);
        var result = await CapacityRecoveryTaskTests.CreateDispatcher(schema.ConnectionString).TickAsync(CancellationToken.None);
        result.Dispatched.ShouldBe(1);
        result.Failures.ShouldBe(0);
        Guid sessionId;
        await using (var verify = CreateContext(schema))
        {
            var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
            stored.Status.ShouldBe(AgentTaskStatus.Dispatched);
            stored.AgentSessionId.ShouldNotBeNull();
            sessionId = stored.AgentSessionId.Value;
            if (wait is null)
            {
                (await verify.CapacityRecoveryWaits.CountAsync(w => w.TaskId == task.Id)).ShouldBe(0);
                return;
            }
            var receipted = await verify.CapacityRecoveryWaits.SingleAsync(w => w.Id == wait.Id);
            receipted.State.ShouldBe(CapacityRecoveryWaitState.StartAccepted);
            receipted.Outcome.ShouldBe("StartAccepted");
            receipted.OutcomeReason.ShouldBe("Dispatch");
            receipted.SessionId.ShouldBe(sessionId);
            receipted.LaunchSessionId.ShouldBe(sessionId);
            receipted.DispatchAttemptId.ShouldNotBeNull();
            receipted.AdmissionCount.ShouldBe(1);
            (await verify.CapacityRecoveryProviderStates.SingleAsync(s => s.Kind == AgentKind.ClaudeCode)).GrantedWaitId.ShouldBeNull();
        }

        // A running dispatch is not a stalled admission, even after its old timeout.
        time.Advance(TimeSpan.FromMinutes(3));
        await service.ReconcileAsync(CancellationToken.None);
        await using (var verify = CreateContext(schema))
            (await verify.CapacityRecoveryWaits.SingleAsync(w => w.Id == wait.Id)).State.ShouldBe(CapacityRecoveryWaitState.StartAccepted);
        await service.ObserveTranscriptAsync(sessionId, TranscriptKinds.UserPrompt, false, CancellationToken.None, sequence: 1);
        await using (var verify = CreateContext(schema))
        {
            var confirmed = await verify.CapacityRecoveryWaits.SingleAsync(w => w.Id == wait.Id);
            confirmed.State.ShouldBe(CapacityRecoveryWaitState.PromptConfirmed);
            confirmed.ConfirmedPromptSequence.ShouldBe(1);
        }
        await service.ObserveTranscriptAsync(sessionId, TranscriptKinds.TurnEnd, false, CancellationToken.None);
        await using (var verify = CreateContext(schema))
        {
            var progressed = await verify.CapacityRecoveryWaits.SingleAsync(w => w.Id == wait.Id);
            progressed.State.ShouldBe(CapacityRecoveryWaitState.Progressed);
            progressed.AdmissionCount.ShouldBe(1);
        }
    }

    private sealed class ThrowOnReceiptSave : SaveChangesInterceptor
    {
        public int Throws;
        public AppDbContext? Context;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is AppDbContext db && db.ChangeTracker.Entries<CapacityRecoveryWait>()
                    .Any(e => e.Entity.State == CapacityRecoveryWaitState.StartAccepted)
                && Interlocked.CompareExchange(ref Throws, 1, 0) == 0)
            {
                Context = db;
                throw new InvalidOperationException("injected receipt save failure");
            }

            return ValueTask.FromResult(result);
        }
    }

    private static async Task<(Guid AgentId, Guid SessionId)> SeedWarmOpusAsync(IsolatedTestSchema schema, string directory)
    {
        var warm = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(schema.ConnectionString, directory);
        await using var db = CreateContext(schema);
        var agent = await db.Agents.SingleAsync(a => a.Id == warm.AgentId);
        agent.ModelLevel = AgentModelLevel.High;
        await db.SaveChangesAsync();
        return warm;
    }

    private static async Task<AgentTask> SeedQueuedOpusAsync(IsolatedTestSchema schema, string directory)
    {
        var (agentId, _) = await SeedWarmOpusAsync(schema, directory);
        return await ModelAvailabilityDispatcherTests.SeedQueuedTaskAsync(
            schema.ConnectionString, directory, agentId, AgentModelLevel.High, "capacity dispatch");
    }

    private static async Task<CapacityRecoveryWait> SeedWaitAsync(IsolatedTestSchema schema, Guid taskId,
        AgentKind kind, string alias, bool liveSession = false, bool ready = true)
    {
        var (capacity, _, provider) = CapacityRecoveryTestSupport.CreateService(schema);
        await using var services = provider;
        return await capacity.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            liveSession ? $"session:{Guid.NewGuid():N}" : $"task:{taskId:N}",
            liveSession ? CapacityWaitConsumerKind.LiveSession : CapacityWaitConsumerKind.QueuedTask,
            executionKind: kind, alias: alias, taskId: taskId, holdAlreadyCleared: ready), CancellationToken.None);
    }
}
