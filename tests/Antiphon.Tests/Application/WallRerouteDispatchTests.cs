using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
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
            session.EffectiveModelId = walledKind == AgentKind.Grok ? "grok-4.6" : "fable";
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
        var wait = await SeedWaitAsync(schema, task.Id, AgentKind.Grok, "grok-4.6", liveSession: true);
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
            alreadyChosen ? AgentKind.ClaudeCode : AgentKind.Grok, alreadyChosen ? "opus" : "grok-4.6");
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
