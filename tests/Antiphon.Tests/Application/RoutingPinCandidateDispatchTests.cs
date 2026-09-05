using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0322 S3: dispatcher re-walk, resume, cleared pin, Required-untouched, reroute, attention.
/// </summary>
[Category("Integration")]
public sealed class RoutingPinCandidateDispatchTests
{
    [Test]
    public async Task Queued_walked_task_rewalks_to_the_next_pin_candidate_at_dispatch()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var pin = await SeedStageListAsync(schema);
        await SeedHoldAsync(schema, "fable", DateTime.UtcNow.AddHours(1));
        var (agentId, _) = await SeedWarmAgentAsync(schema, workspace.Path);
        var task = await SeedQueuedPinTaskAsync(schema, workspace.Path, agentId, pin.Id);
        var dispatcher = CreateDispatcher(schema);

        var result = await dispatcher.TickAsync(CancellationToken.None);

        result.SkippedModelAvailability.ShouldBe(0);
        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched);
        stored.ModelLevel.ShouldBe(AgentModelLevel.High);
        stored.RoutingPinId.ShouldBe(pin.Id);
        var rerouted = await verify.AgentTaskEvents.SingleAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Rerouted);
        rerouted.Detail.ShouldContain("Plan stage pin");
        rerouted.Detail.ShouldContain("at dispatch");
    }

    [Test]
    public async Task All_held_blocks_a_queued_walked_task()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var pin = await SeedStageListAsync(schema);
        await SeedHoldAsync(schema, "fable", null);
        await SeedHoldAsync(schema, "opus", null);
        var (agentId, _) = await SeedWarmAgentAsync(schema, workspace.Path);
        var task = await SeedQueuedPinTaskAsync(schema, workspace.Path, agentId, pin.Id);
        var dispatcher = CreateDispatcher(schema);

        var result = await dispatcher.TickAsync(CancellationToken.None);

        result.BlockedRoutingExhausted.ShouldBe(1);
        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Blocked);
        stored.FailureReason.ShouldContain("Plan pin (human, required)");
    }

    [Test]
    public async Task Hold_cleared_resumes_a_routing_blocked_pin_task()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var pin = await SeedStageListAsync(schema);
        var taskId = Guid.NewGuid();
        await using (var db = CreateContext(schema))
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "blocked pin",
                Goal = "plan it",
                Role = AgentTaskRole.Plan,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier,
                RoutingPinId = pin.Id,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = workspace.Path,
                Status = AgentTaskStatus.Blocked,
                FailureReason = ComplexityRoutingService.RoutingExhaustedPrefix
                    + "stage Plan pin (human, required) — fable held; opus held",
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var dispatcher = CreateDispatcher(schema);
        var result = await dispatcher.TickAsync(CancellationToken.None);

        result.ResumedRoutingBlocked.ShouldBe(1);
        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == taskId);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched);
        stored.FailureReason.ShouldBeNull();
        var rerouted = await verify.AgentTaskEvents.SingleAsync(
            e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Rerouted);
        rerouted.Detail.ShouldContain("capacity returned");
        rerouted.Detail.ShouldContain("Plan stage pin");
    }

    [Test]
    public async Task Cleared_pin_keeps_the_snapshot_and_Holds()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var pin = await SeedStageListAsync(schema);
        await SeedHoldAsync(schema, "fable", DateTime.UtcNow.AddHours(1));
        var (agentId, _) = await SeedWarmAgentAsync(schema, workspace.Path);
        var task = await SeedQueuedPinTaskAsync(schema, workspace.Path, agentId, pin.Id);
        await using (var db = CreateContext(schema))
        {
            var row = await db.RoutingPins.SingleAsync(p => p.Id == pin.Id);
            row.ClearedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var dispatcher = CreateDispatcher(schema);
        var result = await dispatcher.TickAsync(CancellationToken.None);

        result.SkippedModelAvailability.ShouldBe(1);
        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Queued);
        stored.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Held)).ShouldBe(1);
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Rerouted)).ShouldBe(0);
    }

    [Test]
    public async Task Single_candidate_Required_is_Held_and_never_rerouted()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        await using (var db = CreateContext(schema))
        {
            db.RoutingPins.Add(new RoutingPin
            {
                Id = Guid.NewGuid(),
                Role = AgentTaskRole.Plan,
                Provenance = RoutingPinProvenance.Human,
                Strength = RoutingPinStrength.Required,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier,
                Reason = "fable only",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await SeedHoldAsync(schema, "fable", DateTime.UtcNow.AddHours(1));
        var (agentId, _) = await SeedWarmAgentAsync(schema, workspace.Path);
        var task = await SeedQueuedPinTaskAsync(schema, workspace.Path, agentId, routingPinId: null);
        var dispatcher = CreateDispatcher(schema);

        var result = await dispatcher.TickAsync(CancellationToken.None);

        result.SkippedModelAvailability.ShouldBe(1);
        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Rerouted)).ShouldBe(0);
    }

    [Test]
    public async Task Reroute_nulls_RoutingPinId()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var pin = await SeedStageListAsync(schema);
        var taskId = Guid.NewGuid();
        await using (var db = CreateContext(schema))
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "blocked pin",
                Goal = "plan it",
                Role = AgentTaskRole.Plan,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier,
                RoutingPinId = pin.Id,
                ExplicitAgentKind = AgentKind.ClaudeCode,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = workspace.Path,
                Status = AgentTaskStatus.Blocked,
                FailureReason = ComplexityRoutingService.RoutingExhaustedPrefix + "stage Plan pin",
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var summary = await CreateTaskService(schema, workspace)
            .RerouteAsync(taskId, AgentKind.Grok, AgentModelLevel.Frontier, CancellationToken.None);

        summary.AgentKind.ShouldBe(AgentKind.Grok);
        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == taskId);
        stored.RoutingPinId.ShouldBeNull();
        stored.Complexity.ShouldBeNull();
        stored.ExplicitAgentKind.ShouldBeNull();
        stored.ExplicitModelLevel.ShouldBeNull();
        stored.Status.ShouldBe(AgentTaskStatus.Queued);
    }

    [Test]
    public async Task Explicit_kind_rewalk_stays_inside_the_original_ask()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var pin = await SeedStageListAsync(
            schema,
            [
                new(AgentKind.ClaudeCode, AgentModelLevel.Frontier),
                new(AgentKind.ClaudeCode, AgentModelLevel.High),
                new(AgentKind.Grok, AgentModelLevel.Frontier),
            ]);
        await SeedHoldAsync(schema, "fable", null);
        await SeedHoldAsync(schema, "opus", null);
        var taskId = Guid.NewGuid();
        await using (var db = CreateContext(schema))
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "blocked pin ask",
                Goal = "plan it",
                Role = AgentTaskRole.Plan,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier,
                RoutingPinId = pin.Id,
                ExplicitAgentKind = AgentKind.ClaudeCode,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = workspace.Path,
                Status = AgentTaskStatus.Blocked,
                FailureReason = ComplexityRoutingService.RoutingExhaustedPrefix
                    + "stage Plan pin (human, required) — fable held; opus held",
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var dispatcher = CreateDispatcher(schema);
        var result = await dispatcher.TickAsync(CancellationToken.None);

        result.ResumedRoutingBlocked.ShouldBe(0);
        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == taskId);
        stored.Status.ShouldBe(AgentTaskStatus.Blocked);
        stored.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        stored.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Rerouted)).ShouldBe(0);
    }

    [Test]
    public async Task Three_blocked_Plan_pin_tasks_are_one_attention_row()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var pin = new RoutingPin
        {
            Id = Guid.NewGuid(),
            Role = AgentTaskRole.Plan,
            Provenance = RoutingPinProvenance.Human,
            Strength = RoutingPinStrength.Required,
            CandidatesJson = RoutingCandidate.Serialize(
            [
                new(AgentKind.ClaudeCode, AgentModelLevel.Frontier),
                new(AgentKind.ClaudeCode, AgentModelLevel.High),
            ]),
            Reason = "fable then opus",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.RoutingPins.Add(pin);
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var id = Guid.NewGuid();
            ids.Add(id);
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = $"blocked-{i}",
                Goal = "plan it",
                Role = AgentTaskRole.Plan,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier,
                RoutingPinId = pin.Id,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = Path.GetTempPath(),
                Status = AgentTaskStatus.Blocked,
                FailureReason = ComplexityRoutingService.RoutingExhaustedPrefix
                    + "stage Plan pin (human, required) — fable held; opus held",
                CreatedAt = DateTime.UtcNow.AddMinutes(-10 + i),
            });
        }

        await db.SaveChangesAsync();

        var items = await new AttentionService(
            db,
            new RefusingSessionRunnerClient(),
            Options.Create(new SupervisionSettings()),
            Options.Create(new DelegationSettings()),
            TimeProvider.System,
            NullLogger<AttentionService>.Instance).GetAsync(CancellationToken.None);
        var row = items.Items.Single(i => i.Kind == AttentionKind.RoutingExhausted);
        row.Title.ShouldBe("Plan stage pin exhausted");
        row.Headline.ShouldContain("3 tasks waiting");
        row.TaskId.ShouldBe(ids[0]);
    }

    [Test]
    public async Task Pin_plus_chain_blocked_tasks_group_under_pin_plus_chain_key()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var pin = new RoutingPin
        {
            Id = Guid.NewGuid(),
            Role = AgentTaskRole.Plan,
            Provenance = RoutingPinProvenance.Human,
            Strength = RoutingPinStrength.Preferred,
            CandidatesJson = RoutingCandidate.Serialize(
            [
                new(AgentKind.ClaudeCode, AgentModelLevel.Frontier),
                new(AgentKind.ClaudeCode, AgentModelLevel.High),
            ]),
            Reason = "fable then opus",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.RoutingPins.Add(pin);
        var ids = new List<Guid>();
        for (var i = 0; i < 2; i++)
        {
            var id = Guid.NewGuid();
            ids.Add(id);
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = $"pin-chain-{i}",
                Goal = "plan it",
                Role = AgentTaskRole.Plan,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier,
                Complexity = TaskComplexity.Hard,
                RoutingPinId = pin.Id,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = Path.GetTempPath(),
                Status = AgentTaskStatus.Blocked,
                FailureReason = ComplexityRoutingService.RoutingExhaustedPrefix
                    + "CARD-0301 Plan/Hard pin+chain — fable held; opus held",
                CreatedAt = DateTime.UtcNow.AddMinutes(-10 + i),
            });
        }

        await db.SaveChangesAsync();

        var items = await new AttentionService(
            db,
            new RefusingSessionRunnerClient(),
            Options.Create(new SupervisionSettings()),
            Options.Create(new DelegationSettings()),
            TimeProvider.System,
            NullLogger<AttentionService>.Instance).GetAsync(CancellationToken.None);
        var row = items.Items.Single(i => i.Kind == AttentionKind.RoutingExhausted);
        row.Title.ShouldBe("Plan/Hard pin+chain exhausted");
        row.Headline.ShouldContain("2 tasks waiting");
        row.TaskId.ShouldBe(ids[0]);
    }

    [Test]
    public async Task Wall_on_a_walked_pin_task_requeues_on_the_next_candidate()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var pin = await SeedStageListAsync(schema);
        var taskId = Guid.NewGuid();
        await using (var db = CreateContext(schema))
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "working pin",
                Goal = "plan it",
                Role = AgentTaskRole.Plan,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier,
                RoutingPinId = pin.Id,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = workspace.Path,
                Status = AgentTaskStatus.Working,
                CreatedAt = DateTime.UtcNow,
                DispatchedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using var db2 = CreateContext(schema);
        var settings = Options.Create(new DelegationSettings { AllowedRoots = [workspace.Path] });
        var availability = new ModelAvailability(db2, TimeProvider.System, NullLogger<ModelAvailability>.Instance);
        var routing = new ComplexityRoutingService(db2, settings, TimeProvider.System, availability);
        var pins = new RoutingPinService(db2, TimeProvider.System, NullLogger<RoutingPinService>.Instance);
        var tasks = new AgentTaskService(
            db2,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            settings,
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance,
            modelAvailability: availability,
            routingPins: pins,
            complexityRouting: routing);
        var task = await db2.AgentTasks.SingleAsync(t => t.Id == taskId);

        db2.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = Guid.NewGuid(),
            Kind = AgentKind.ClaudeCode,
            ModelAlias = "fable",
            Source = ModelAvailabilitySource.AutoDetected,
            HitAt = DateTime.UtcNow,
            Reason = "wall",
        });
        await db2.SaveChangesAsync();

        var decision = await tasks.RerouteOnWallAsync(
            task, "fable", "fable hit a usage wall", sessionLimitHasScheduledResume: false, CancellationToken.None);

        decision.Kind.ShouldBe(AgentTaskService.WallRerouteKind.Rerouted);
        decision.NewAlias.ShouldBe("opus");
        var stored = await db2.AgentTasks.SingleAsync(t => t.Id == taskId);
        stored.Status.ShouldBe(AgentTaskStatus.Queued);
        stored.ModelLevel.ShouldBe(AgentModelLevel.High);
        stored.RoutingPinId.ShouldBe(pin.Id);
        stored.FailureReason.ShouldContain("Plan stage pin");
    }

    [Test]
    public async Task Wall_rewalk_stays_inside_the_original_explicit_kind()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var pin = await SeedStageListAsync(
            schema,
            [
                new(AgentKind.ClaudeCode, AgentModelLevel.Frontier),
                new(AgentKind.ClaudeCode, AgentModelLevel.High),
                new(AgentKind.Grok, AgentModelLevel.Frontier),
            ]);
        var taskId = Guid.NewGuid();
        await using (var db = CreateContext(schema))
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "working pin ask",
                Goal = "plan it",
                Role = AgentTaskRole.Plan,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier,
                RoutingPinId = pin.Id,
                ExplicitAgentKind = AgentKind.ClaudeCode,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = workspace.Path,
                Status = AgentTaskStatus.Working,
                CreatedAt = DateTime.UtcNow,
                DispatchedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using var db2 = CreateContext(schema);
        var settings = Options.Create(new DelegationSettings { AllowedRoots = [workspace.Path] });
        var availability = new ModelAvailability(db2, TimeProvider.System, NullLogger<ModelAvailability>.Instance);
        var routing = new ComplexityRoutingService(db2, settings, TimeProvider.System, availability);
        var pins = new RoutingPinService(db2, TimeProvider.System, NullLogger<RoutingPinService>.Instance);
        var tasks = new AgentTaskService(
            db2,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            settings,
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance,
            modelAvailability: availability,
            routingPins: pins,
            complexityRouting: routing);
        var task = await db2.AgentTasks.SingleAsync(t => t.Id == taskId);

        db2.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = Guid.NewGuid(),
            Kind = AgentKind.ClaudeCode,
            ModelAlias = "fable",
            Source = ModelAvailabilitySource.AutoDetected,
            HitAt = DateTime.UtcNow,
            Reason = "wall",
        });
        db2.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = Guid.NewGuid(),
            Kind = AgentKind.ClaudeCode,
            ModelAlias = "opus",
            Source = ModelAvailabilitySource.Manual,
            HitAt = DateTime.UtcNow,
            Reason = "held",
        });
        await db2.SaveChangesAsync();

        var decision = await tasks.RerouteOnWallAsync(
            task, "fable", "fable hit a usage wall", sessionLimitHasScheduledResume: false, CancellationToken.None);

        decision.Kind.ShouldBe(AgentTaskService.WallRerouteKind.Blocked);
        var stored = await db2.AgentTasks.SingleAsync(t => t.Id == taskId);
        stored.Status.ShouldBe(AgentTaskStatus.Blocked);
        stored.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        stored.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
    }

    private static async Task<RoutingPin> SeedStageListAsync(
        IsolatedTestSchema schema,
        IReadOnlyList<RoutingCandidate>? candidates = null)
    {
        await using var db = CreateContext(schema);
        var pin = new RoutingPin
        {
            Id = Guid.NewGuid(),
            Role = AgentTaskRole.Plan,
            Provenance = RoutingPinProvenance.Human,
            Strength = RoutingPinStrength.Required,
            CandidatesJson = RoutingCandidate.Serialize(
                candidates ??
                [
                    new(AgentKind.ClaudeCode, AgentModelLevel.Frontier),
                    new(AgentKind.ClaudeCode, AgentModelLevel.High),
                ]),
            Reason = "fable then opus",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.RoutingPins.Add(pin);
        await db.SaveChangesAsync();
        return pin;
    }

    private static async Task SeedHoldAsync(IsolatedTestSchema schema, string alias, DateTime? until)
    {
        await using var db = CreateContext(schema);
        db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = Guid.NewGuid(),
            Kind = AgentKind.ClaudeCode,
            ModelAlias = alias,
            Source = ModelAvailabilitySource.Manual,
            DisabledUntil = until,
            HitAt = DateTime.UtcNow,
            Reason = "manual hold",
        });
        await db.SaveChangesAsync();
    }

    private static async Task<AgentTask> SeedQueuedPinTaskAsync(
        IsolatedTestSchema schema, string directory, Guid pinnedAgentId, Guid? routingPinId)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "pin walked task",
            Goal = "plan it",
            Role = AgentTaskRole.Plan,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Frontier,
            RoutingPinId = routingPinId,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            Status = AgentTaskStatus.Queued,
            AgentId = pinnedAgentId,
            Ephemeral = false,
            CreatedAt = DateTime.UtcNow,
        };
        await using var db = CreateContext(schema);
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task<(Guid AgentId, Guid SessionId)> SeedWarmAgentAsync(
        IsolatedTestSchema schema, string directory)
    {
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateContext(schema);
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = directory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = $"task-{agentId:N}"[..13],
            Slug = $"task-{agentId:N}"[..13],
            WorkingDirectory = directory,
            Details = "Warm pool delegate.",
            Status = AgentStatus.Idle,
            Kind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.High,
            IsPoolDelegate = true,
            PoolIdleSince = now.AddMinutes(-3),
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return (agentId, sessionId);
    }

    private static AgentTaskDispatcher CreateDispatcher(IsolatedTestSchema schema)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(schema.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            PoolReservedForCallerMinutes = 2,
            PoolIdleRetireMinutes = 5,
            PoolMaxIdlePerDirectory = 3,
            MaxConcurrentTasks = 512,
        }));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-pin-tick"),
        });
        services.AddScoped<AgentTaskService>();
        services.AddScoped<RoutingPinService>();
        services.AddScoped<ModelAvailability>();
        services.AddScoped<ComplexityRoutingService>();
        services.AddScoped<AgentTaskDispatcher>();
        return services.BuildServiceProvider().CreateScope().ServiceProvider
            .GetRequiredService<AgentTaskDispatcher>();
    }

    private static AgentTaskService CreateTaskService(IsolatedTestSchema schema, TempWorkspace workspace)
    {
        var db = CreateContext(schema);
        var settings = Options.Create(new DelegationSettings { AllowedRoots = [workspace.Path] });
        var availability = new ModelAvailability(db, TimeProvider.System, NullLogger<ModelAvailability>.Instance);
        return new AgentTaskService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            settings,
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance,
            modelAvailability: availability);
    }

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-pin-tick").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
