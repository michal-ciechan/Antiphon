using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1496: a chain task created with an explicit standing agent launches on that agent's
/// <see cref="Agent.ModelId"/> wherever the walk sends it. When that model is held, Retry used to
/// kill the session (chain tasks were exempt from the CARD-1490 refusal) and the usage-wall reroute
/// used to requeue onto the held agent model. Controls: an unheld agent model and an ephemeral
/// agent keep today's path. Isolated schema per test (chain index plus holds).
/// </summary>
[Category("Integration")]
public class HeldStandingAgentChainTests
{
    [Test]
    [Arguments(AgentTaskStatus.Working)]
    [Arguments(AgentTaskStatus.Failed)]
    public async Task Retrying_a_chain_task_on_a_standing_agent_whose_model_is_held_is_409_with_no_kill(AgentTaskStatus status)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await SeedHoldAsync(db, ModelAlias.Fable, new DateTime(2099, 10, 23, 8, 0, 0, DateTimeKind.Utc));
        var agentId = await SeedAgentAsync(db, ModelAlias.Fable, ephemeral: false);
        var sessionId = Guid.NewGuid();
        var task = await SeedChainTaskAsync(db, workspace.Path, status, sessionId, agentId, ephemeral: false);
        var stopper = new RecordingSessionStopper();

        var ex = await Should.ThrowAsync<ModelDisabledException>(
            () => CreateService(db, workspace.Path, stopper).RetryAsync(task.Id, CancellationToken.None),
            "C1496-retry-standing-held-refused");

        ex.Code.ShouldBe("model_disabled", "C1496-retry-code");
        ex.Hold.ModelAlias.ShouldBe(ModelAlias.Fable, "C1496-retry-names-agent-alias");
        ex.Message.ShouldContain("fable is disabled until 2099-10-23T08:00:00Z", customMessage: "C1496-retry-names-until");
        stopper.Killed.ShouldBeEmpty("C1496-retry-no-kill");
        var after = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        after.Status.ShouldBe(status, "C1496-retry-status-unchanged");
        after.Attempt.ShouldBe(task.Attempt, "C1496-retry-attempt-unchanged");
        after.ModelLevel.ShouldBe(AgentModelLevel.Frontier, "C1496-retry-level-unchanged");
        after.AgentId.ShouldBe(agentId, "C1496-retry-agent-kept");
        after.AgentSessionId.ShouldBe(sessionId, "C1496-retry-session-kept");
        (await db.AgentTaskEvents.AsNoTracking().CountAsync(e => e.AgentTaskId == task.Id))
            .ShouldBe(0, "C1496-retry-no-event");
    }

    [Test]
    [Arguments(false, ModelAlias.Opus)]
    [Arguments(true, ModelAlias.Fable)]
    public async Task Retrying_a_chain_task_requeues_as_today_when_no_standing_agent_model_is_held(bool ephemeral, string modelId)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await SeedHoldAsync(db, ModelAlias.Fable, DateTime.UtcNow.AddHours(1));
        var agentId = await SeedAgentAsync(db, modelId, ephemeral);
        var sessionId = Guid.NewGuid();
        var task = await SeedChainTaskAsync(db, workspace.Path, AgentTaskStatus.Failed, sessionId, agentId, ephemeral);
        var stopper = new RecordingSessionStopper();

        var summary = await CreateService(db, workspace.Path, stopper).RetryAsync(task.Id, CancellationToken.None);

        summary.Status.ShouldBe(AgentTaskStatus.Queued, "C1496-retry-control-requeued");
        var after = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        after.Attempt.ShouldBe(task.Attempt + 1, "C1496-retry-control-attempt");
        (await db.AgentTaskEvents.AsNoTracking().CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Retried))
            .ShouldBe(1, "C1496-retry-control-event");
    }

    // The wall wrote the fable hold; the Hard walk skips fable and picks Claude High (opus). On a
    // standing agent pinned to fable that requeue would launch on the held model again.
    [Test]
    public async Task Wall_reroute_of_a_chain_task_on_a_standing_agent_whose_model_is_held_blocks_and_launches_nothing()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        await WallRerouteFixture.SeedHardChainAsync(schema);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await SeedHoldAsync(db, ModelAlias.Fable, new DateTime(2099, 10, 23, 8, 0, 0, DateTimeKind.Utc));
        var agentId = await SeedAgentAsync(db, ModelAlias.Fable, ephemeral: false);
        var sessionId = Guid.NewGuid();
        var task = await SeedChainTaskAsync(db, workspace.Path, AgentTaskStatus.Working, sessionId, agentId, ephemeral: false);

        var decision = await CreateService(db, workspace.Path, new RecordingSessionStopper()).RerouteOnWallAsync(
            await db.AgentTasks.SingleAsync(t => t.Id == task.Id), ModelAlias.Fable, "usage wall", false,
            CancellationToken.None);

        decision.Kind.ShouldBe(AgentTaskService.WallRerouteKind.Blocked, "C1496-wall-standing-held-blocked");
        db.ChangeTracker.Clear();
        var after = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        after.Status.ShouldBe(AgentTaskStatus.Blocked, "C1496-wall-status-blocked");
        after.FailureReason.ShouldNotBeNull("C1496-wall-reason-set");
        after.FailureReason.ShouldStartWith(AgentTaskService.HeldStandingAgentModelPrefix, customMessage: "C1496-wall-named-reason");
        after.FailureReason.ShouldContain("fable", customMessage: "C1496-wall-reason-names-model");
        after.FailureReason.ShouldNotStartWith(ComplexityRoutingService.RoutingExhaustedPrefix,
            customMessage: "C1496-wall-not-auto-resumed");
        after.AgentKind.ShouldBe(AgentKind.ClaudeCode, "C1496-wall-kind-unchanged");
        after.ModelLevel.ShouldBe(AgentModelLevel.Frontier, "C1496-wall-level-unchanged");
        after.Attempt.ShouldBe(task.Attempt, "C1496-wall-no-new-attempt");
        after.AgentId.ShouldBe(agentId, "C1496-wall-agent-kept");
        (await db.AgentTaskEvents.AsNoTracking().CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Rerouted))
            .ShouldBe(0, "C1496-wall-no-reroute");
        (await db.AgentTaskEvents.AsNoTracking().CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Blocked))
            .ShouldBe(1, "C1496-wall-one-block");
    }

    [Test]
    [Arguments(false, ModelAlias.Opus)]
    [Arguments(true, ModelAlias.Fable)]
    public async Task Wall_reroute_of_a_chain_task_requeues_as_today_when_no_standing_agent_model_is_held(bool ephemeral, string modelId)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        await WallRerouteFixture.SeedHardChainAsync(schema);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await SeedHoldAsync(db, ModelAlias.Fable, DateTime.UtcNow.AddHours(1));
        var agentId = await SeedAgentAsync(db, modelId, ephemeral);
        var sessionId = Guid.NewGuid();
        var task = await SeedChainTaskAsync(db, workspace.Path, AgentTaskStatus.Working, sessionId, agentId, ephemeral);
        var stopper = new RecordingSessionStopper();

        var decision = await CreateService(db, workspace.Path, stopper).RerouteOnWallAsync(
            await db.AgentTasks.SingleAsync(t => t.Id == task.Id), ModelAlias.Fable, "usage wall", false,
            CancellationToken.None);

        decision.Kind.ShouldBe(AgentTaskService.WallRerouteKind.Rerouted, "C1496-wall-control-rerouted");
        decision.NewAlias.ShouldBe(ModelAlias.Opus, "C1496-wall-control-next-candidate");
        stopper.Killed.ShouldBe([sessionId], "C1496-wall-control-walled-session-stopped");
        db.ChangeTracker.Clear();
        var after = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        after.Status.ShouldBe(AgentTaskStatus.Queued, "C1496-wall-control-queued");
        after.ModelLevel.ShouldBe(AgentModelLevel.High, "C1496-wall-control-level");
    }

    private static async Task<Guid> SeedAgentAsync(AppDbContext db, string modelId, bool ephemeral)
    {
        var slug = (ephemeral ? "ephemeral-" : "standing-") + modelId;
        var agent = new Agent
        {
            Id = Guid.NewGuid(), Name = slug, Slug = slug,
            Kind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Frontier, ModelId = modelId,
            WorkingDirectory = Path.GetTempPath(), Status = AgentStatus.Running,
        };
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return agent.Id;
    }

    private static async Task SeedHoldAsync(AppDbContext db, string alias, DateTime until)
    {
        db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = Guid.NewGuid(),
            Kind = AgentKind.ClaudeCode,
            ModelAlias = alias,
            Source = ModelAvailabilitySource.Manual,
            DisabledUntil = until,
            HitAt = DateTime.UtcNow.AddHours(-1),
            Reason = "fable usage exhausted",
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task<AgentTask> SeedChainTaskAsync(
        AppDbContext db, string directory, AgentTaskStatus status, Guid sessionId, Guid agentId, bool ephemeral)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "Held standing agent chain",
            Goal = "Seeded goal.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Custom,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Frontier,
            Complexity = TaskComplexity.Hard,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            Status = status,
            AgentSessionId = sessionId,
            DispatchedAt = DateTime.UtcNow.AddMinutes(-30),
            CreatedAt = DateTime.UtcNow.AddMinutes(-31),
            AgentId = agentId,
            Ephemeral = ephemeral,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return task;
    }

    private static AgentTaskService CreateService(AppDbContext db, string directory, RecordingSessionStopper stopper)
    {
        var settings = Options.Create(new DelegationSettings
        {
            MaxDepth = 5, MaxTasksPerRoot = 40, AllowedRoots = [directory],
        });
        var availability = new ModelAvailability(db, TimeProvider.System, NullLogger<ModelAvailability>.Instance);
        return new AgentTaskService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            settings,
            new MockEventBus(),
            stopper,
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance,
            modelAvailability: availability,
            routingPins: new RoutingPinService(db, TimeProvider.System, NullLogger<RoutingPinService>.Instance),
            complexityRouting: new ComplexityRoutingService(db, settings, TimeProvider.System, availability));
    }
}
