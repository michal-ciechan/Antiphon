using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
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
/// CARD-0090 S3: dispatcher re-walk, Blocked-on-exhaust, resume, Required-untouched, reroute.
/// Isolated schema: unique chain index.
/// </summary>
[Category("Integration")]
public class ComplexityDispatcherTests
{
    [Test]
    public async Task A_queued_Plan_Hard_task_rewalks_a_replaced_cell_and_names_Plan_Hard_chain()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        await SeedChainAsync(schema, TaskComplexity.Hard, AgentTaskRole.Plan,
            (AgentKind.ClaudeCode, AgentModelLevel.Frontier),
            (AgentKind.Grok, AgentModelLevel.Frontier));
        await SeedHoldAsync(schema, "fable", DateTime.UtcNow.AddHours(1));
        var (agentId, _) = await SeedWarmAgentAsync(schema, workspace.Path);
        var task = await SeedQueuedChainTaskAsync(schema, workspace.Path, agentId);
        await ReplaceChainAsync(schema, AgentTaskRole.Plan, TaskComplexity.Hard,
            (AgentKind.ClaudeCode, AgentModelLevel.High));
        var dispatcher = CreateDispatcher(schema);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.ModelLevel.ShouldBe(AgentModelLevel.High);
        stored.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        var rerouted = await verify.AgentTaskEvents.SingleAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Rerouted);
        rerouted.Detail.ShouldContain("Plan/Hard chain");
    }

    [Test]
    public async Task A_blocked_Plan_Hard_task_resumes_when_the_operator_adds_an_available_cell()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var taskId = Guid.NewGuid();
        await using (var db = CreateContext(schema))
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "blocked plan hard",
                Goal = "plan it",
                Role = AgentTaskRole.Plan,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier,
                Complexity = TaskComplexity.Hard,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = workspace.Path,
                Status = AgentTaskStatus.Blocked,
                FailureReason = ComplexityRoutingService.RoutingExhaustedPrefix
                    + "Plan/Hard chain is empty (no Plan/Hard row, no any-role Hard row, no config default).",
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await SeedChainAsync(schema, TaskComplexity.Hard, AgentTaskRole.Plan,
            (AgentKind.ClaudeCode, AgentModelLevel.Frontier));
        var dispatcher = CreateDispatcher(schema);

        var result = await dispatcher.TickAsync(CancellationToken.None);

        result.ResumedRoutingBlocked.ShouldBe(1);
        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == taskId);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched);
        stored.FailureReason.ShouldBeNull();
        var rerouted = await verify.AgentTaskEvents.SingleAsync(
            e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Rerouted);
        rerouted.Detail.ShouldContain("Plan/Hard chain");
        rerouted.Detail.ShouldContain("capacity returned");
    }

    [Test]
    public async Task A_held_head_is_rerouted_to_the_next_candidate_at_dispatch()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        await SeedHardChainAsync(schema);
        await SeedHoldAsync(schema, "fable", DateTime.UtcNow.AddHours(1));
        var (agentId, _) = await SeedWarmAgentAsync(schema, workspace.Path);
        var task = await SeedQueuedChainTaskAsync(schema, workspace.Path, agentId);
        var dispatcher = CreateDispatcher(schema);

        var result = await dispatcher.TickAsync(CancellationToken.None);

        result.SkippedModelAvailability.ShouldBe(0);
        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched);
        stored.ModelLevel.ShouldBe(AgentModelLevel.High);
        stored.Complexity.ShouldBe(TaskComplexity.Hard);
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Rerouted)).ShouldBe(1);
    }

    // CARD-1513: the walk skips a held tier alias and returns Rerouted, but a standing agent
    // still launches its own ModelId. Falling through used to deliver that held model.
    [Test]
    public async Task A_queued_chain_on_a_standing_agent_whose_model_is_held_blocks_instead_of_spawning_after_rewalk()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        await SeedHardChainAsync(schema);
        var until = new DateTime(2099, 10, 23, 8, 0, 0, DateTimeKind.Utc);
        await SeedHoldAsync(schema, ModelAlias.Fable, until);
        var (agentId, sessionId) = await SeedStandingModelAgentAsync(schema, workspace.Path, ModelAlias.Fable);
        var task = await SeedQueuedChainTaskAsync(schema, workspace.Path, agentId);
        var dispatcher = CreateDispatcher(schema);

        var result = await dispatcher.TickAsync(CancellationToken.None);

        result.Dispatched.ShouldBe(0, "C1513-tick-no-spawn");
        result.BlockedRoutingExhausted.ShouldBe(1, "C1513-tick-accounted");
        result.SkippedModelAvailability.ShouldBe(0, "C1513-tick-not-parked");
        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Blocked, "C1513-tick-standing-held-blocked");
        stored.FailureReason.ShouldNotBeNull("C1513-tick-reason-set");
        stored.FailureReason.ShouldStartWith(
            AgentTaskService.HeldStandingAgentModelPrefix, customMessage: "C1513-tick-named-reason");
        stored.FailureReason.ShouldContain("fable", customMessage: "C1513-tick-names-model");
        stored.FailureReason.ShouldContain("until 2099-10-23T08:00:00Z", customMessage: "C1513-tick-names-until");
        stored.FailureReason.ShouldContain("Not rerouted", customMessage: "C1513-tick-not-rerouted");
        stored.FailureReason.ShouldNotStartWith(
            ComplexityRoutingService.RoutingExhaustedPrefix, customMessage: "C1513-tick-not-auto-resumed");
        stored.AgentKind.ShouldBe(AgentKind.ClaudeCode, "C1513-tick-kind-unchanged");
        stored.ModelLevel.ShouldBe(AgentModelLevel.Frontier, "C1513-tick-level-unchanged");
        stored.Attempt.ShouldBe(task.Attempt, "C1513-tick-no-new-attempt");
        stored.AgentId.ShouldBe(agentId, "C1513-tick-agent-kept");
        stored.AgentSessionId.ShouldBeNull("C1513-tick-session-not-adopted");
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Rerouted))
            .ShouldBe(0, "C1513-tick-no-reroute-event");
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Blocked))
            .ShouldBe(1, "C1513-tick-one-block");
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Dispatched))
            .ShouldBe(0, "C1513-tick-no-dispatched-event");
        (await verify.AgentSessions.CountAsync(s => s.Id == sessionId)).ShouldBe(1, "C1513-tick-no-extra-session");
        (await verify.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == sessionId))
            .ShouldBe(0, "C1513-tick-no-brief");
    }

    [Test]
    public async Task An_ephemeral_agent_whose_model_is_held_still_rewalks_and_dispatches()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        await SeedHardChainAsync(schema);
        await SeedHoldAsync(schema, ModelAlias.Fable, DateTime.UtcNow.AddHours(1));
        var (agentId, sessionId) = await SeedStandingModelAgentAsync(schema, workspace.Path, ModelAlias.Fable);
        var task = await SeedQueuedChainTaskAsync(schema, workspace.Path, agentId, ephemeral: true);
        var dispatcher = CreateDispatcher(schema);

        var result = await dispatcher.TickAsync(CancellationToken.None);

        result.BlockedRoutingExhausted.ShouldBe(0, "C1513-ephemeral-not-blocked");
        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched, "C1513-ephemeral-dispatched");
        stored.ModelLevel.ShouldBe(AgentModelLevel.High, "C1513-ephemeral-next-candidate");
        stored.AgentSessionId.ShouldBe(sessionId, "C1513-ephemeral-live-session");
        stored.FailureReason.ShouldBeNull("C1513-ephemeral-no-block-reason");
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Rerouted))
            .ShouldBe(1, "C1513-ephemeral-rerouted");
    }

    [Test]
    public async Task All_held_blocks_the_queued_chain_task()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        await SeedHardChainAsync(schema);
        await SeedHoldAsync(schema, "fable", null);
        await SeedHoldAsync(schema, "opus", null);
        await SeedHoldAsync(schema, "grok-4.7", null, AgentKind.Grok);
        var (agentId, _) = await SeedWarmAgentAsync(schema, workspace.Path);
        var task = await SeedQueuedChainTaskAsync(schema, workspace.Path, agentId);
        var dispatcher = CreateDispatcher(schema);

        var result = await dispatcher.TickAsync(CancellationToken.None);

        result.BlockedRoutingExhausted.ShouldBe(1);
        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Blocked);
        stored.FailureReason.ShouldContain(ComplexityRoutingService.RoutingExhaustedPrefix);
    }

    [Test]
    public async Task Clearing_a_hold_requeues_a_routing_blocked_task()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        await SeedHardChainAsync(schema);
        var taskId = Guid.NewGuid();
        await using (var db = CreateContext(schema))
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "blocked hard",
                Goal = "plan it",
                Role = AgentTaskRole.Plan,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier,
                Complexity = TaskComplexity.Hard,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = workspace.Path,
                Status = AgentTaskStatus.Blocked,
                FailureReason = ComplexityRoutingService.RoutingExhaustedPrefix + "Hard chain — all held",
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
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Rerouted)).ShouldBe(1);
    }

    [Test]
    public async Task A_required_pinned_chain_task_stays_Queued_with_Held_when_the_pin_is_held()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        await SeedHardChainAsync(schema);
        Card card;
        await using (var db = CreateContext(schema))
            card = await RoutingPinServiceTests.SeedCardAsync(db, "CARD-0301");
        await using (var db = CreateContext(schema))
        {
            db.RoutingPins.Add(new RoutingPin
            {
                Id = Guid.NewGuid(),
                CardId = card.Id,
                Role = AgentTaskRole.Plan,
                Provenance = RoutingPinProvenance.Human,
                Strength = RoutingPinStrength.Required,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier,
                Reason = "stays on fable",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await SeedHoldAsync(schema, "fable", DateTime.UtcNow.AddHours(1));
        var (agentId, _) = await SeedWarmAgentAsync(schema, workspace.Path);
        var task = await SeedQueuedChainTaskAsync(schema, workspace.Path, agentId, card.Id);
        var dispatcher = CreateDispatcher(schema);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Queued);
        stored.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Rerouted)).ShouldBe(0);
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Held)).ShouldBeGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task Reroute_on_Blocked_queues_with_Complexity_null()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var taskId = Guid.NewGuid();
        await using (var db = CreateContext(schema))
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "reroute me",
                Goal = "plan it",
                Role = AgentTaskRole.Plan,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier,
                Complexity = TaskComplexity.Hard,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = workspace.Path,
                Status = AgentTaskStatus.Blocked,
                FailureReason = ComplexityRoutingService.RoutingExhaustedPrefix + "Hard chain",
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var summary = await CreateTaskService(schema, workspace).RerouteAsync(
            taskId, AgentKind.Grok, AgentModelLevel.Frontier, CancellationToken.None);

        summary.Status.ShouldBe(AgentTaskStatus.Queued);
        summary.AgentKind.ShouldBe(AgentKind.Grok);
        summary.Complexity.ShouldBeNull();
        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == taskId);
        stored.Complexity.ShouldBeNull();
        stored.FailureReason.ShouldBeNull();
        (await verify.AgentTaskEvents.SingleAsync(
            e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Rerouted))
            .Detail.ShouldContain("explicit");
    }

    [Test]
    public async Task Reroute_to_a_held_alias_is_409()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        await SeedHoldAsync(schema, "fable", DateTime.UtcNow.AddHours(1));
        var taskId = Guid.NewGuid();
        await using (var db = CreateContext(schema))
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "reroute held",
                Goal = "plan it",
                Role = AgentTaskRole.Plan,
                AgentKind = AgentKind.Grok,
                ModelLevel = AgentModelLevel.Frontier,
                Complexity = TaskComplexity.Hard,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = workspace.Path,
                Status = AgentTaskStatus.Blocked,
                FailureReason = ComplexityRoutingService.RoutingExhaustedPrefix + "Hard chain",
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var ex = await Should.ThrowAsync<ModelDisabledException>(() =>
            CreateTaskService(schema, workspace).RerouteAsync(
                taskId, AgentKind.ClaudeCode, AgentModelLevel.Frontier, CancellationToken.None));
        ex.Code.ShouldBe("model_disabled");
    }

    [Test]
    public async Task Reroute_on_Working_is_409()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var taskId = Guid.NewGuid();
        await using (var db = CreateContext(schema))
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "working",
                Goal = "plan it",
                Role = AgentTaskRole.Plan,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier,
                Complexity = TaskComplexity.Hard,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = workspace.Path,
                Status = AgentTaskStatus.Working,
                CreatedAt = DateTime.UtcNow,
                DispatchedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var ex = await Should.ThrowAsync<ConflictException>(() =>
            CreateTaskService(schema, workspace).RerouteAsync(
                taskId, AgentKind.Grok, AgentModelLevel.Frontier, CancellationToken.None));
        ex.StatusCode.ShouldBe(409);
        ex.Message.ShouldContain("Working");
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
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-complexity-tick"),
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

    private static Task SeedHardChainAsync(IsolatedTestSchema schema) =>
        SeedChainAsync(schema, TaskComplexity.Hard, role: null,
            (AgentKind.ClaudeCode, AgentModelLevel.Frontier),
            (AgentKind.ClaudeCode, AgentModelLevel.High),
            (AgentKind.Grok, AgentModelLevel.Frontier));

    private static async Task SeedChainAsync(
        IsolatedTestSchema schema,
        TaskComplexity complexity,
        AgentTaskRole? role,
        params (AgentKind Kind, AgentModelLevel Level)[] pairs)
    {
        await using var db = CreateContext(schema);
        db.ComplexityChains.Add(new ComplexityChain
        {
            Id = Guid.NewGuid(),
            Role = role,
            Complexity = complexity,
            CandidatesJson = ComplexityChain.SerializeCandidates(
                pairs.Select(p => new ComplexityCandidatePair(p.Kind, p.Level)).ToList()),
            Provenance = RoutingPinProvenance.Human,
            Reason = "test",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task ReplaceChainAsync(
        IsolatedTestSchema schema,
        AgentTaskRole? role,
        TaskComplexity complexity,
        params (AgentKind Kind, AgentModelLevel Level)[] pairs)
    {
        await using var db = CreateContext(schema);
        var row = await db.ComplexityChains.SingleAsync(c =>
            c.ClearedAt == null && c.Complexity == complexity && c.Role == role);
        row.CandidatesJson = ComplexityChain.SerializeCandidates(
            pairs.Select(p => new ComplexityCandidatePair(p.Kind, p.Level)).ToList());
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static async Task SeedHoldAsync(
        IsolatedTestSchema schema, string alias, DateTime? until, AgentKind kind = AgentKind.ClaudeCode)
    {
        await using var db = CreateContext(schema);
        db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            ModelAlias = alias,
            Source = ModelAvailabilitySource.Manual,
            DisabledUntil = until,
            HitAt = DateTime.UtcNow,
            Reason = "manual hold",
        });
        await db.SaveChangesAsync();
    }

    private static async Task<AgentTask> SeedQueuedChainTaskAsync(
        IsolatedTestSchema schema, string directory, Guid pinnedAgentId, Guid? cardId = null, bool ephemeral = false)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "hard chain task",
            Goal = "plan it",
            Role = AgentTaskRole.Plan,
            CardId = cardId,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Frontier,
            Complexity = TaskComplexity.Hard,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            Status = AgentTaskStatus.Queued,
            AgentId = pinnedAgentId,
            Ephemeral = ephemeral,
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
            PoolClaudeEffort = "high",
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return (agentId, sessionId);
    }

    private static async Task<(Guid AgentId, Guid SessionId)> SeedStandingModelAgentAsync(
        IsolatedTestSchema schema, string directory, string modelId)
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
            Name = "standing-fable",
            Slug = $"standing-{agentId:N}"[..20],
            WorkingDirectory = directory,
            Details = "A standing agent pinned to a held model.",
            Status = AgentStatus.Running,
            Kind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Frontier,
            ModelId = modelId,
            IsPoolDelegate = false,
            AlwaysOn = false,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return (agentId, sessionId);
    }

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-complexity-tick").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
