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
/// CARD-1490: an explicit escalation must not land a task on a held alias. Create already
/// refuses a held explicit target (ModelAvailabilityCreateTests); escalation used to kill the
/// running session, move the task to the held tier and leave it parked behind the hold.
/// Isolated schema per test.
/// </summary>
[Category("Integration")]
public class HeldModelEscalationTests
{
    [Test]
    [Arguments(AgentTaskStatus.Failed)]
    [Arguments(AgentTaskStatus.Working)]
    public async Task Escalating_onto_a_held_alias_is_409_and_leaves_the_task_untouched(AgentTaskStatus status)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var until = new DateTime(2099, 10, 23, 8, 0, 0, DateTimeKind.Utc);
        await SeedHoldAsync(db, until);
        var sessionId = Guid.NewGuid();
        var task = await SeedTaskAsync(db, status, sessionId);
        var stopper = new RecordingSessionStopper();

        var ex = await Should.ThrowAsync<ModelDisabledException>(
            () => CreateService(db, stopper).EscalateAsync(task.Id, null, CancellationToken.None),
            "C1490-escalate-held-refused");

        ex.Code.ShouldBe("model_disabled", "C1490-escalate-code");
        ex.Message.ShouldContain("fable is disabled until 2099-10-23T08:00:00Z", customMessage: "C1490-escalate-names-alias-until");
        ex.Hold.Kind.ShouldBe(AgentKind.ClaudeCode, "C1490-escalate-names-kind");
        ex.Available.ShouldContain(ModelAlias.Opus, "C1490-escalate-available");
        stopper.Killed.ShouldBeEmpty("C1490-escalate-no-kill");
        var after = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        after.ModelLevel.ShouldBe(AgentModelLevel.High, "C1490-escalate-level-unchanged");
        after.AgentKind.ShouldBe(AgentKind.ClaudeCode, "C1490-escalate-no-kind-substitution");
        after.Status.ShouldBe(status, "C1490-escalate-status-unchanged");
        after.EscalatedFrom.ShouldBeNull("C1490-escalate-not-stamped");
        after.AgentSessionId.ShouldBe(sessionId, "C1490-escalate-session-kept");
    }

    [Test]
    public async Task Escalating_after_the_hold_lapsed_is_unchanged()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await SeedHoldAsync(db, DateTime.UtcNow.AddMinutes(-5));
        var task = await SeedTaskAsync(db, AgentTaskStatus.Failed, Guid.NewGuid());

        var summary = await CreateService(db, new RecordingSessionStopper())
            .EscalateAsync(task.Id, null, CancellationToken.None);

        summary.ModelLevel.ShouldBe(AgentModelLevel.Frontier, "C1490-lapsed-escalates");
        summary.EscalatedFrom.ShouldBe(AgentModelLevel.High, "C1490-lapsed-stamped");
        summary.Status.ShouldBe(AgentTaskStatus.Queued, "C1490-lapsed-requeued");
    }

    [Test]
    public async Task Escalating_onto_a_non_held_alias_is_unchanged_while_another_alias_is_held()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await SeedHoldAsync(db, DateTime.UtcNow.AddHours(1));
        var task = await SeedTaskAsync(db, AgentTaskStatus.Failed, Guid.NewGuid(), AgentModelLevel.Medium);

        var summary = await CreateService(db, new RecordingSessionStopper())
            .EscalateAsync(task.Id, null, CancellationToken.None);

        summary.ModelLevel.ShouldBe(AgentModelLevel.High, "C1490-non-held-escalates");
        summary.Status.ShouldBe(AgentTaskStatus.Queued, "C1490-non-held-requeued");
    }

    // CARD-1490 D-1: Retry requeues at the same tier and the requeue stops the live session first.
    [Test]
    [Arguments(AgentTaskStatus.Working)]
    [Arguments(AgentTaskStatus.Failed)]
    public async Task Retrying_onto_a_held_alias_is_409_with_no_kill_and_no_change(AgentTaskStatus status)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await SeedHoldAsync(db, new DateTime(2099, 10, 23, 8, 0, 0, DateTimeKind.Utc));
        var sessionId = Guid.NewGuid();
        var task = await SeedTaskAsync(db, status, sessionId, AgentModelLevel.Frontier);
        var stopper = new RecordingSessionStopper();

        var ex = await Should.ThrowAsync<ModelDisabledException>(
            () => CreateService(db, stopper).RetryAsync(task.Id, CancellationToken.None),
            "C1490-retry-held-refused");

        ex.Code.ShouldBe("model_disabled", "C1490-retry-code");
        ex.Message.ShouldContain("fable is disabled until 2099-10-23T08:00:00Z", customMessage: "C1490-retry-names-alias-until");
        ex.Available.ShouldContain(ModelAlias.Opus, "C1490-retry-available");
        stopper.Killed.ShouldBeEmpty("C1490-retry-no-kill");
        var after = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        after.Status.ShouldBe(status, "C1490-retry-status-unchanged");
        after.Attempt.ShouldBe(task.Attempt, "C1490-retry-attempt-unchanged");
        after.ModelLevel.ShouldBe(AgentModelLevel.Frontier, "C1490-retry-level-unchanged");
        after.AgentSessionId.ShouldBe(sessionId, "C1490-retry-session-kept");
        (await db.AgentTaskEvents.AsNoTracking().CountAsync(e => e.AgentTaskId == task.Id))
            .ShouldBe(0, "C1490-retry-no-event");
    }

    // A list-governed task re-walks to an unheld candidate at the tick; Retry keeps requeueing it.
    [Test]
    public async Task Retrying_a_list_governed_task_on_a_held_alias_still_requeues_for_the_rewalk()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await SeedHoldAsync(db, DateTime.UtcNow.AddHours(1));
        var task = await SeedTaskAsync(db, AgentTaskStatus.Failed, Guid.NewGuid(), AgentModelLevel.Frontier,
            complexity: TaskComplexity.Hard);

        var summary = await CreateService(db, new RecordingSessionStopper())
            .RetryAsync(task.Id, CancellationToken.None);

        summary.Status.ShouldBe(AgentTaskStatus.Queued, "C1490-retry-chain-requeued");
    }

    // CARD-1490 D-2: the hold check keys on the alias the tick will launch. A pinned agent's
    // ModelId wins the tier ladder (AgentTaskDispatcher dispatch alias), both ways.
    [Test]
    public async Task Escalating_a_pinned_agent_task_is_409_when_the_agent_model_is_held()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await SeedHoldAsync(db, DateTime.UtcNow.AddHours(1), ModelAlias.Opus);
        var agentId = await SeedPinnedAgentAsync(db, ModelAlias.Opus);
        var sessionId = Guid.NewGuid();
        var task = await SeedTaskAsync(db, AgentTaskStatus.Working, sessionId, agentId: agentId);
        var stopper = new RecordingSessionStopper();

        var ex = await Should.ThrowAsync<ModelDisabledException>(
            () => CreateService(db, stopper).EscalateAsync(task.Id, null, CancellationToken.None),
            "C1490-pinned-held-refused");

        ex.Hold.ModelAlias.ShouldBe(ModelAlias.Opus, "C1490-pinned-names-agent-alias");
        stopper.Killed.ShouldBeEmpty("C1490-pinned-no-kill");
        var after = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        after.Status.ShouldBe(AgentTaskStatus.Working, "C1490-pinned-status-unchanged");
        after.ModelLevel.ShouldBe(AgentModelLevel.High, "C1490-pinned-level-unchanged");
        after.AgentSessionId.ShouldBe(sessionId, "C1490-pinned-session-kept");
    }

    [Test]
    public async Task Escalating_a_pinned_agent_task_is_unchanged_when_only_the_tier_alias_is_held()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await SeedHoldAsync(db, DateTime.UtcNow.AddHours(1));
        var agentId = await SeedPinnedAgentAsync(db, ModelAlias.Opus);
        var task = await SeedTaskAsync(db, AgentTaskStatus.Failed, Guid.NewGuid(), agentId: agentId);

        var summary = await CreateService(db, new RecordingSessionStopper())
            .EscalateAsync(task.Id, null, CancellationToken.None);

        summary.ModelLevel.ShouldBe(AgentModelLevel.Frontier, "C1490-pinned-tier-held-escalates");
        summary.Status.ShouldBe(AgentTaskStatus.Queued, "C1490-pinned-tier-held-requeued");
    }

    // A specialist launches its recorded alias whatever the tier says.
    [Test]
    public async Task Escalating_a_specialist_task_is_409_when_its_recorded_alias_is_held()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await SeedHoldAsync(db, DateTime.UtcNow.AddHours(1), ModelAlias.Opus);
        var sessionId = Guid.NewGuid();
        var task = await SeedTaskAsync(db, AgentTaskStatus.Working, sessionId, specialistAlias: ModelAlias.Opus);
        var stopper = new RecordingSessionStopper();

        var ex = await Should.ThrowAsync<ModelDisabledException>(
            () => CreateService(db, stopper).EscalateAsync(task.Id, null, CancellationToken.None),
            "C1490-specialist-held-refused");

        ex.Hold.ModelAlias.ShouldBe(ModelAlias.Opus, "C1490-specialist-names-alias");
        stopper.Killed.ShouldBeEmpty("C1490-specialist-no-kill");
        var after = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        after.ModelLevel.ShouldBe(AgentModelLevel.High, "C1490-specialist-level-unchanged");
        after.AgentSessionId.ShouldBe(sessionId, "C1490-specialist-session-kept");
    }

    private static async Task<Guid> SeedPinnedAgentAsync(AppDbContext db, string modelId)
    {
        var agent = new Agent
        {
            Id = Guid.NewGuid(), Name = "pinned-opus", Slug = "pinned-opus",
            Kind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.High, ModelId = modelId,
            WorkingDirectory = Path.GetTempPath(), Status = AgentStatus.Running,
        };
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return agent.Id;
    }

    private static async Task SeedHoldAsync(AppDbContext db, DateTime until, string alias = ModelAlias.Fable)
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
    }

    private static async Task<AgentTask> SeedTaskAsync(
        AppDbContext db, AgentTaskStatus status, Guid sessionId, AgentModelLevel level = AgentModelLevel.High,
        Guid? agentId = null, string? specialistAlias = null, TaskComplexity? complexity = null)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "Held escalation",
            Goal = "Seeded goal.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Custom,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = level,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.GetTempPath(),
            Status = status,
            AgentSessionId = sessionId,
            DispatchedAt = DateTime.UtcNow.AddMinutes(-30),
            CreatedAt = DateTime.UtcNow.AddMinutes(-31),
            AgentId = agentId,
            Ephemeral = agentId is null,
            SpecialistModelAlias = specialistAlias,
            Complexity = complexity,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return task;
    }

    private static AgentTaskService CreateService(AppDbContext db, RecordingSessionStopper stopper) =>
        new(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(new DelegationSettings { MaxDepth = 5, MaxTasksPerRoot = 40, AllowedRoots = [] }),
            new MockEventBus(),
            stopper,
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance,
            modelAvailability: new ModelAvailability(db, TimeProvider.System, NullLogger<ModelAvailability>.Instance));
}
