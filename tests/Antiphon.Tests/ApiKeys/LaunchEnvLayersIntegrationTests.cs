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

namespace Antiphon.Tests.ApiKeys;

/// <summary>
/// CARD-0106 gaps 1 and 2 — launch-time override surface, project defaults, drop-guards.
/// Merge order itself is pinned in <see cref="AgentLaunchEnvTests"/> (registry) and
/// <see cref="ApiKeyLaunchPathTests"/> (managed profile). These pin the wiring.
/// </summary>
[Category("Integration")]
public sealed class LaunchEnvLayersIntegrationTests
{
    private static CancellationToken Ct => CancellationToken.None;

    [Test]
    public async Task BuildLaunchSpec_carries_the_task_override_and_keeps_ANTIPHON_identity()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildDispatcherProvider(schema.ConnectionString);
        var (dispatcher, _) = DispatcherOf(provider);

        var task = new AgentTask
        {
            Id = Guid.NewGuid(),
            Title = "override",
            Goal = "override",
            WorkingDirectory = Path.GetTempPath(),
            LaunchEnvOverrideJson = AgentLaunchEnv.Serialize(new Dictionary<string, string>
            {
                ["ANTHROPIC_BASE_URL"] = "http://proxy:8080",
                ["ANTIPHON_SESSION_ID"] = "hijacked",
            }),
            CreatedAt = DateTime.UtcNow,
        };
        task.RootTaskId = task.Id;
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "pool",
            Slug = "pool",
            WorkingDirectory = task.WorkingDirectory,
            IsPoolDelegate = true,
            LaunchEnvJson = AgentLaunchEnv.Serialize(new Dictionary<string, string>
            {
                ["ANTHROPIC_BASE_URL"] = "https://api.anthropic.com",
            }),
        };
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Starting,
            Cwd = task.WorkingDirectory,
            Cols = 120,
            Rows = 30,
        };
        AgentTaskService.RawTokens[task.Id] = "the-real-token";

        var spec = dispatcher.BuildLaunchSpec(task, agent, session);

        spec.Env["ANTHROPIC_BASE_URL"].ShouldBe("http://proxy:8080");
        spec.Env["ANTIPHON_SESSION_ID"].ShouldBe(session.Id.ToString("D"));
        spec.Env["ANTIPHON_TASK_TOKEN"].ShouldBe("the-real-token");
        spec.Env["ANTIPHON_TASK_ID"].ShouldBe(task.Id.ToString("D"));
    }

    [Test]
    public async Task CreateAsync_persists_the_override_and_refuses_ANTIPHON_names_at_the_boundary()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        using var workspace = new TempWorkspace();
        var service = CreateTaskService(db, [workspace.Path]);

        var created = await service.CreateAsync(
            new CreateAgentTaskRequest(
                Goal: "use the proxy",
                WorkingDirectory: workspace.Path,
                LaunchEnvOverride: new Dictionary<string, string>
                {
                    ["ANTHROPIC_BASE_URL"] = "http://proxy:8080",
                }),
            new AgentTaskService.Caller(null, null, workspace.Path),
            Ct);

        var stored = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id, Ct);
        AgentLaunchEnv.Parse(stored.LaunchEnvOverrideJson)["ANTHROPIC_BASE_URL"]
            .ShouldBe("http://proxy:8080");

        var ex = await Should.ThrowAsync<ValidationException>(() => service.CreateAsync(
            new CreateAgentTaskRequest(
                Goal: "steal the token",
                WorkingDirectory: workspace.Path,
                LaunchEnvOverride: new Dictionary<string, string>
                {
                    ["ANTIPHON_TASK_TOKEN"] = "stolen",
                }),
            new AgentTaskService.Caller(null, null, workspace.Path),
            Ct));
        ex.StatusCode.ShouldBe(422);
        ex.Errors.Values.SelectMany(e => e).ShouldContain(e => e.Contains("ANTIPHON_TASK_TOKEN"));
    }

    [Test]
    public async Task FollowUpOnTask_plus_an_override_is_refused_422()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        using var workspace = new TempWorkspace();
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = $"task-{Guid.NewGuid():N}"[..13],
            Slug = $"pool-{Guid.NewGuid():N}"[..13],
            WorkingDirectory = workspace.Path,
            Details = "Warm pool delegate.",
            Status = AgentStatus.Idle,
            IsPoolDelegate = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Agents.Add(agent);
        var prior = new AgentTask
        {
            Id = Guid.NewGuid(),
            Title = "prior",
            Goal = "prior",
            WorkingDirectory = workspace.Path,
            Status = AgentTaskStatus.Succeeded,
            AgentId = agent.Id,
            CreatedAt = DateTime.UtcNow,
        };
        prior.RootTaskId = prior.Id;
        db.AgentTasks.Add(prior);
        await db.SaveChangesAsync(Ct);

        var service = CreateTaskService(db, [workspace.Path]);
        var ex = await Should.ThrowAsync<ValidationException>(() => service.CreateAsync(
            new CreateAgentTaskRequest(
                Goal: "follow up with an override",
                WorkingDirectory: workspace.Path,
                FollowUpOnTask: DelegationReportFormatter.Short(prior.Id),
                LaunchEnvOverride: new Dictionary<string, string>
                {
                    ["ANTHROPIC_BASE_URL"] = "http://proxy:8080",
                }),
            new AgentTaskService.Caller(null, null, workspace.Path),
            Ct));

        ex.StatusCode.ShouldBe(422);
        ex.Errors.Values.SelectMany(e => e).ShouldContain(e => e.Contains("follow-up"));
    }

    [Test]
    public async Task TryReuseWarmAgentAsync_declines_a_non_empty_override_and_not_an_empty_one()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildDispatcherProvider(schema.ConnectionString);
        var (dispatcher, db) = DispatcherOf(provider);
        using var workspace = new TempWorkspace();
        var now = DateTime.UtcNow;
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = workspace.Path,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        };
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = $"task-{Guid.NewGuid():N}"[..13],
            Slug = $"pool-{Guid.NewGuid():N}"[..13],
            WorkingDirectory = workspace.Path,
            Status = AgentStatus.Idle,
            IsPoolDelegate = true,
            Kind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            PoolIdleSince = now.AddMinutes(-10),
            PersistentSessionId = session.Id.ToString("D"),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AgentSessions.Add(session);
        db.Agents.Add(agent);
        await db.SaveChangesAsync(Ct);

        var withOverride = NewQueued(workspace.Path, AgentModelLevel.Medium);
        withOverride.LaunchEnvOverrideJson = AgentLaunchEnv.Serialize(
            new Dictionary<string, string> { ["ANTHROPIC_BASE_URL"] = "http://proxy:8080" });
        var empty = NewQueued(workspace.Path, AgentModelLevel.Medium);
        empty.LaunchEnvOverrideJson = "{}";
        db.AgentTasks.AddRange(withOverride, empty);
        await db.SaveChangesAsync(Ct);

        (await dispatcher.TryReuseWarmAgentAsync(withOverride, now, Ct))
            .ShouldBe(AgentTaskDispatcher.ReuseOutcome.SpawnFresh);
        (await dispatcher.TryReuseWarmAgentAsync(empty, now, Ct))
            .ShouldBe(AgentTaskDispatcher.ReuseOutcome.Reused);
    }

    [Test]
    public async Task CreateAsync_snapshots_the_session_caller_env_filtered_to_the_inherit_list()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        using var workspace = new TempWorkspace();
        var sessionId = Guid.NewGuid();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "grok",
            AgentKind = AgentKind.Grok,
            Status = SessionStatus.Running,
            Cwd = workspace.Path,
            Cols = 120,
            Rows = 30,
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
        });
        db.Agents.Add(new Agent
        {
            Id = Guid.NewGuid(),
            Name = "pm-orchestrator",
            Slug = "pm-orchestrator",
            WorkingDirectory = workspace.Path,
            PersistentSessionId = sessionId.ToString("D"),
            LaunchEnvJson = AgentLaunchEnv.Serialize(new Dictionary<string, string>
            {
                ["X_LLM_PROJECT"] = "PredictionMarkets",
                ["GROK_BASE_URL"] = "http://localhost:10746/v1",
                ["UNRELATED"] = "must-drop",
            }),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(Ct);

        var service = CreateTaskService(db, [workspace.Path]);
        var created = await service.CreateAsync(
            new CreateAgentTaskRequest(Goal: "plan the next slice", Role: AgentTaskRole.Plan),
            new AgentTaskService.Caller(null, sessionId, workspace.Path),
            Ct);

        var stored = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id, Ct);
        var inherited = AgentLaunchEnv.Parse(stored.InheritedLaunchEnvJson);
        inherited["X_LLM_PROJECT"].ShouldBe("PredictionMarkets");
        inherited["GROK_BASE_URL"].ShouldBe("http://localhost:10746/v1");
        inherited.ShouldNotContainKey("UNRELATED");
    }

    [Test]
    public async Task CreateAsync_task_token_layers_parent_override_over_parent_agent_env()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        using var workspace = new TempWorkspace();
        var parentAgent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "parent-orch",
            Slug = $"orch-{Guid.NewGuid():N}"[..16],
            WorkingDirectory = workspace.Path,
            Kind = AgentKind.ClaudeCode,
            LaunchEnvJson = AgentLaunchEnv.Serialize(new Dictionary<string, string>
            {
                ["X_LLM_PROJECT"] = "FromAgent",
                ["ANTHROPIC_BASE_URL"] = "http://localhost:10746",
            }),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Agents.Add(parentAgent);
        var parent = new AgentTask
        {
            Id = Guid.NewGuid(),
            Title = "parent",
            Goal = "parent",
            Kind = AgentTaskKind.Orchestrator,
            WorkingDirectory = workspace.Path,
            AgentId = parentAgent.Id,
            LaunchEnvOverrideJson = AgentLaunchEnv.Serialize(new Dictionary<string, string>
            {
                ["X_LLM_PROJECT"] = "FromOverride",
            }),
            CreatedAt = DateTime.UtcNow,
        };
        parent.RootTaskId = parent.Id;
        db.AgentTasks.Add(parent);
        await db.SaveChangesAsync(Ct);

        var service = CreateTaskService(db, [workspace.Path]);
        var created = await service.CreateAsync(
            new CreateAgentTaskRequest(Goal: "child work", WorkingDirectory: workspace.Path),
            new AgentTaskService.Caller(parent, Guid.NewGuid(), workspace.Path),
            Ct);

        var stored = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id, Ct);
        var inherited = AgentLaunchEnv.Parse(stored.InheritedLaunchEnvJson);
        inherited["X_LLM_PROJECT"].ShouldBe("FromOverride");
        inherited["ANTHROPIC_BASE_URL"].ShouldBe("http://localhost:10746");
    }

    [Test]
    public async Task FollowUpOnTask_computes_no_inherited_snapshot()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        using var workspace = new TempWorkspace();
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = $"task-{Guid.NewGuid():N}"[..13],
            Slug = $"pool-{Guid.NewGuid():N}"[..13],
            WorkingDirectory = workspace.Path,
            Details = "Warm pool delegate.",
            Status = AgentStatus.Idle,
            IsPoolDelegate = true,
            LaunchEnvJson = AgentLaunchEnv.Serialize(new Dictionary<string, string>
            {
                ["X_LLM_PROJECT"] = "ShouldNotCopy",
            }),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Agents.Add(agent);
        var prior = new AgentTask
        {
            Id = Guid.NewGuid(),
            Title = "prior",
            Goal = "prior",
            WorkingDirectory = workspace.Path,
            Status = AgentTaskStatus.Succeeded,
            AgentId = agent.Id,
            CreatedAt = DateTime.UtcNow,
        };
        prior.RootTaskId = prior.Id;
        db.AgentTasks.Add(prior);
        await db.SaveChangesAsync(Ct);

        var service = CreateTaskService(db, [workspace.Path]);
        var created = await service.CreateAsync(
            new CreateAgentTaskRequest(
                Goal: "follow up",
                WorkingDirectory: workspace.Path,
                FollowUpOnTask: DelegationReportFormatter.Short(prior.Id)),
            new AgentTaskService.Caller(null, null, workspace.Path),
            Ct);

        var stored = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id, Ct);
        AgentLaunchEnv.Parse(stored.InheritedLaunchEnvJson).ShouldBeEmpty();
    }

    [Test]
    public async Task a_standing_agent_pin_computes_no_inherited_snapshot()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        using var workspace = new TempWorkspace();
        var standing = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "standing-specialist",
            Slug = "standing-specialist",
            WorkingDirectory = workspace.Path,
            IsPoolDelegate = false,
            LaunchEnvJson = AgentLaunchEnv.Serialize(new Dictionary<string, string>
            {
                ["X_LLM_PROJECT"] = "ShouldNotCopy",
            }),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Agents.Add(standing);
        await db.SaveChangesAsync(Ct);

        var service = CreateTaskService(db, [workspace.Path]);
        var created = await service.CreateAsync(
            new CreateAgentTaskRequest(
                Goal: "pin to standing",
                WorkingDirectory: workspace.Path,
                AgentId: standing.Id),
            new AgentTaskService.Caller(null, null, workspace.Path),
            Ct);

        var stored = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id, Ct);
        AgentLaunchEnv.Parse(stored.InheritedLaunchEnvJson).ShouldBeEmpty();
    }

    [Test]
    public async Task CreateAsync_refuses_a_local_proxy_preview_without_an_LLM_project_marker()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        using var workspace = new TempWorkspace();
        var project = await AddProjectAsync(db, new Dictionary<string, string>
        {
            ["ANTHROPIC_BASE_URL"] = "http://localhost:10746/v1",
        });
        var parent = NewParentOrchestrator(workspace.Path, project.Id);
        db.AgentTasks.Add(parent);
        await db.SaveChangesAsync(Ct);

        var service = CreateTaskService(db, [workspace.Path], ApiKeys(db));
        var ex = await Should.ThrowAsync<ValidationException>(() => service.CreateAsync(
            new CreateAgentTaskRequest(Goal: "use proxy", WorkingDirectory: workspace.Path),
            new AgentTaskService.Caller(parent, Guid.NewGuid(), workspace.Path),
            Ct));

        ex.StatusCode.ShouldBe(422);
        ex.Code.ShouldBe("llm_project_required");
        ex.Errors["X_LLM_PROJECT"].Single().ShouldContain("ANTHROPIC_BASE_URL");
    }

    [Test]
    public async Task CreateAsync_allows_a_local_proxy_preview_when_override_supplies_the_project_marker()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        using var workspace = new TempWorkspace();
        var project = await AddProjectAsync(db, new Dictionary<string, string>
        {
            ["ANTHROPIC_BASE_URL"] = "http://127.0.0.1:10746/v1",
        });
        var parent = NewParentOrchestrator(workspace.Path, project.Id);
        db.AgentTasks.Add(parent);
        await db.SaveChangesAsync(Ct);

        var created = await CreateTaskService(db, [workspace.Path], ApiKeys(db)).CreateAsync(
            new CreateAgentTaskRequest(
                Goal: "use proxy",
                WorkingDirectory: workspace.Path,
                LaunchEnvOverride: new Dictionary<string, string> { ["X_LLM_PROJECT"] = "PredictionMarkets" }),
            new AgentTaskService.Caller(parent, Guid.NewGuid(), workspace.Path),
            Ct);

        created.Warning.ShouldBeNull();
    }

    [Test]
    public async Task CreateAsync_does_not_refuse_a_non_local_proxy_url_without_a_project_marker()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        using var workspace = new TempWorkspace();
        var project = await AddProjectAsync(db, new Dictionary<string, string>
        {
            ["ANTHROPIC_BASE_URL"] = "https://proxy.example.test/v1",
        });
        var parent = NewParentOrchestrator(workspace.Path, project.Id);
        db.AgentTasks.Add(parent);
        await db.SaveChangesAsync(Ct);

        var created = await CreateTaskService(db, [workspace.Path], ApiKeys(db)).CreateAsync(
            new CreateAgentTaskRequest(Goal: "use remote proxy", WorkingDirectory: workspace.Path),
            new AgentTaskService.Caller(parent, Guid.NewGuid(), workspace.Path),
            Ct);

        created.Warning.ShouldBeNull();
    }

    [Test]
    public async Task CreateAsync_warns_when_a_marker_has_no_Claude_proxy_route()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        using var workspace = new TempWorkspace();
        var parentAgent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "marker-parent",
            Slug = $"marker-{Guid.NewGuid():N}"[..16],
            WorkingDirectory = workspace.Path,
            LaunchEnvJson = AgentLaunchEnv.Serialize(new Dictionary<string, string>
            {
                ["X_LLM_PROJECT"] = "PredictionMarkets",
            }),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var parent = NewParentOrchestrator(workspace.Path, null);
        parent.AgentId = parentAgent.Id;
        db.AddRange(parentAgent, parent);
        await db.SaveChangesAsync(Ct);

        var created = await CreateTaskService(db, [workspace.Path]).CreateAsync(
            new CreateAgentTaskRequest(Goal: "wrapper fallback", WorkingDirectory: workspace.Path),
            new AgentTaskService.Caller(parent, Guid.NewGuid(), workspace.Path),
            Ct);

        created.Warning.ShouldNotBeNull();
        created.Warning.ShouldContain("ANTHROPIC_BASE_URL");
        var storedEvents = await db.AgentTaskEvents.Where(e => e.AgentTaskId == created.Id).ToListAsync(Ct);
        storedEvents.ShouldContain(e => e.Type == AgentTaskEventType.Warning && e.Detail!.Contains("ANTHROPIC_BASE_URL"));
    }

    [Test]
    public async Task CreateAsync_prefers_the_supplied_live_LLM_env_over_server_side_reconstruction()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        using var workspace = new TempWorkspace();
        var parentAgent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "stored-parent",
            Slug = $"stored-{Guid.NewGuid():N}"[..16],
            WorkingDirectory = workspace.Path,
            LaunchEnvJson = AgentLaunchEnv.Serialize(new Dictionary<string, string>
            {
                ["X_LLM_PROJECT"] = "StoredProject",
            }),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var parent = NewParentOrchestrator(workspace.Path, null);
        parent.AgentId = parentAgent.Id;
        db.AddRange(parentAgent, parent);
        await db.SaveChangesAsync(Ct);

        var created = await CreateTaskService(db, [workspace.Path]).CreateAsync(
            new CreateAgentTaskRequest(
                Goal: "use live project",
                WorkingDirectory: workspace.Path,
                InheritedLlmEnv: new Dictionary<string, string> { ["X_LLM_PROJECT"] = "LiveProject" }),
            new AgentTaskService.Caller(parent, Guid.NewGuid(), workspace.Path),
            Ct);

        var stored = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id, Ct);
        AgentLaunchEnv.Parse(stored.InheritedLaunchEnvJson)["X_LLM_PROJECT"].ShouldBe("LiveProject");
    }

    [Test]
    public async Task CreateAsync_drops_unknown_supplied_LLM_env_names_with_a_warning()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        using var workspace = new TempWorkspace();

        var created = await CreateTaskService(db, [workspace.Path]).CreateAsync(
            new CreateAgentTaskRequest(
                Goal: "use live project",
                WorkingDirectory: workspace.Path,
                InheritedLlmEnv: new Dictionary<string, string>
                {
                    ["X_LLM_PROJECT"] = "LiveProject",
                    ["NOT_ROUTING"] = "discard",
                }),
            new AgentTaskService.Caller(null, null, workspace.Path),
            Ct);

        var stored = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id, Ct);
        AgentLaunchEnv.Parse(stored.InheritedLaunchEnvJson).ShouldNotContainKey("NOT_ROUTING");
        created.Warning.ShouldContain("NOT_ROUTING");
    }

    [Test]
    public async Task CreateAsync_refuses_ANTIPHON_names_in_supplied_LLM_env()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        using var workspace = new TempWorkspace();

        var ex = await Should.ThrowAsync<ValidationException>(() => CreateTaskService(db, [workspace.Path]).CreateAsync(
            new CreateAgentTaskRequest(
                Goal: "override plumbing",
                WorkingDirectory: workspace.Path,
                InheritedLlmEnv: new Dictionary<string, string> { ["ANTIPHON_TASK_TOKEN"] = "no" }),
            new AgentTaskService.Caller(null, null, workspace.Path),
            Ct));

        ex.StatusCode.ShouldBe(422);
        ex.Errors["inheritedLlmEnv"].Single().ShouldContain("ANTIPHON_TASK_TOKEN");
    }

    [Test]
    public async Task BuildLaunchSpec_carries_inherited_env_and_the_override_still_wins()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildDispatcherProvider(schema.ConnectionString);
        var (dispatcher, _) = DispatcherOf(provider);

        var task = new AgentTask
        {
            Id = Guid.NewGuid(),
            Title = "inherit",
            Goal = "inherit",
            WorkingDirectory = Path.GetTempPath(),
            InheritedLaunchEnvJson = AgentLaunchEnv.Serialize(new Dictionary<string, string>
            {
                ["X_LLM_PROJECT"] = "PredictionMarkets",
                ["ANTHROPIC_BASE_URL"] = "http://inherited:8080",
            }),
            LaunchEnvOverrideJson = AgentLaunchEnv.Serialize(new Dictionary<string, string>
            {
                ["X_LLM_PROJECT"] = "Other",
            }),
            CreatedAt = DateTime.UtcNow,
        };
        task.RootTaskId = task.Id;
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "pool",
            Slug = "pool",
            WorkingDirectory = task.WorkingDirectory,
            IsPoolDelegate = true,
        };
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Starting,
            Cwd = task.WorkingDirectory,
            Cols = 120,
            Rows = 30,
        };

        var spec = dispatcher.BuildLaunchSpec(task, agent, session);

        spec.Env["ANTHROPIC_BASE_URL"].ShouldBe("http://inherited:8080");
        spec.Env["X_LLM_PROJECT"].ShouldBe("Other");
    }

    [Test]
    public async Task BuildLaunchSpecAsync_carries_inherited_env_on_the_profile_less_path()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildDispatcherProvider(schema.ConnectionString, withApiKeys: true);
        var (dispatcher, _) = DispatcherOf(provider);

        var task = NewQueued(Path.GetTempPath(), AgentModelLevel.High);
        task.InheritedLaunchEnvJson = AgentLaunchEnv.Serialize(new Dictionary<string, string>
        {
            ["X_LLM_PROJECT"] = "PredictionMarkets",
            ["GROK_BASE_URL"] = "http://localhost:10746/v1",
        });
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "pool",
            Slug = $"pool-{Guid.NewGuid():N}"[..16],
            WorkingDirectory = task.WorkingDirectory,
            IsPoolDelegate = true,
        };
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Starting,
            Cwd = task.WorkingDirectory,
            Cols = 120,
            Rows = 30,
        };

        var spec = await dispatcher.BuildLaunchSpecAsync(
            task,
            agent,
            session,
            new AgentTaskDispatcher.DelegateProgram(AgentKind.ClaudeCode, "claude", null),
            attachedBundleKeys: null,
            Ct);

        spec.Env["X_LLM_PROJECT"].ShouldBe("PredictionMarkets");
        spec.Env["GROK_BASE_URL"].ShouldBe("http://localhost:10746/v1");
    }

    [Test]
    public async Task the_child_agent_env_beats_inherited_and_inherited_beats_the_project_default()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildDispatcherProvider(schema.ConnectionString, withApiKeys: true);
        var (dispatcher, db) = DispatcherOf(provider);
        var project = await AddProjectAsync(db, new Dictionary<string, string>
        {
            ["X_LLM_PROJECT"] = "FromProject",
            ["ANTHROPIC_BASE_URL"] = "http://from-project",
        });

        var task = NewQueued(Path.GetTempPath(), AgentModelLevel.High);
        task.ProjectId = project.Id;
        task.InheritedLaunchEnvJson = AgentLaunchEnv.Serialize(new Dictionary<string, string>
        {
            ["X_LLM_PROJECT"] = "FromInherited",
            ["ANTHROPIC_BASE_URL"] = "http://from-inherited",
        });
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "pool",
            Slug = $"pool-{Guid.NewGuid():N}"[..16],
            WorkingDirectory = task.WorkingDirectory,
            IsPoolDelegate = true,
            LaunchEnvJson = AgentLaunchEnv.Serialize(new Dictionary<string, string>
            {
                ["X_LLM_PROJECT"] = "FromAgent",
            }),
        };
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Starting,
            Cwd = task.WorkingDirectory,
            Cols = 120,
            Rows = 30,
        };

        var spec = await dispatcher.BuildLaunchSpecAsync(
            task,
            agent,
            session,
            new AgentTaskDispatcher.DelegateProgram(AgentKind.ClaudeCode, "claude", null),
            null,
            Ct);

        spec.Env["X_LLM_PROJECT"].ShouldBe("FromAgent");
        spec.Env["ANTHROPIC_BASE_URL"].ShouldBe("http://from-inherited");
    }

    [Test]
    public async Task TryReuseWarmAgentAsync_declines_when_inherited_env_projections_differ()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildDispatcherProvider(schema.ConnectionString);
        var (dispatcher, db) = DispatcherOf(provider);
        using var workspace = new TempWorkspace();
        var now = DateTime.UtcNow;
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = workspace.Path,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        };
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = $"task-{Guid.NewGuid():N}"[..13],
            Slug = $"pool-{Guid.NewGuid():N}"[..13],
            WorkingDirectory = workspace.Path,
            Status = AgentStatus.Idle,
            IsPoolDelegate = true,
            Kind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            PoolIdleSince = now.AddMinutes(-10),
            PersistentSessionId = session.Id.ToString("D"),
            LaunchEnvJson = AgentLaunchEnv.Serialize(new Dictionary<string, string>
            {
                ["X_LLM_PROJECT"] = "PredictionMarkets",
            }),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AgentSessions.Add(session);
        db.Agents.Add(agent);
        await db.SaveChangesAsync(Ct);

        var matching = NewQueued(workspace.Path, AgentModelLevel.Medium);
        matching.InheritedLaunchEnvJson = AgentLaunchEnv.Serialize(new Dictionary<string, string>
        {
            ["X_LLM_PROJECT"] = "PredictionMarkets",
        });
        var different = NewQueued(workspace.Path, AgentModelLevel.Medium);
        different.InheritedLaunchEnvJson = AgentLaunchEnv.Serialize(new Dictionary<string, string>
        {
            ["X_LLM_PROJECT"] = "Other",
        });
        var empty = NewQueued(workspace.Path, AgentModelLevel.Medium);
        db.AgentTasks.AddRange(matching, different, empty);
        await db.SaveChangesAsync(Ct);

        // Mismatches first: SpawnFresh does not claim the warm row, so the matching
        // case can still reuse it afterwards.
        (await dispatcher.TryReuseWarmAgentAsync(different, now, Ct))
            .ShouldBe(AgentTaskDispatcher.ReuseOutcome.SpawnFresh);
        (await dispatcher.TryReuseWarmAgentAsync(empty, now, Ct))
            .ShouldBe(AgentTaskDispatcher.ReuseOutcome.SpawnFresh,
                "a {}-stamped caller must not reuse a process launched with a project marker");
        (await dispatcher.TryReuseWarmAgentAsync(matching, now, Ct))
            .ShouldBe(AgentTaskDispatcher.ReuseOutcome.Reused);
    }

    [Test]
    public async Task PlaceOnStandingAgentAsync_warns_when_inherited_names_differ_and_never_values()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildDispatcherProvider(schema.ConnectionString);
        var (dispatcher, db) = DispatcherOf(provider);
        using var workspace = new TempWorkspace();
        var now = DateTime.UtcNow;
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = workspace.Path,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        };
        var standing = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "standing-specialist",
            Slug = "standing-specialist",
            WorkingDirectory = workspace.Path,
            Status = AgentStatus.Running,
            IsPoolDelegate = false,
            Kind = AgentKind.ClaudeCode,
            PersistentSessionId = session.Id.ToString("D"),
            LaunchEnvJson = AgentLaunchEnv.Serialize(new Dictionary<string, string>
            {
                ["X_LLM_PROJECT"] = "StandingProject",
            }),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AgentSessions.Add(session);
        db.Agents.Add(standing);
        var claimed = NewQueued(workspace.Path, AgentModelLevel.High);
        claimed.AgentId = standing.Id;
        claimed.InheritedLaunchEnvJson = AgentLaunchEnv.Serialize(new Dictionary<string, string>
        {
            ["X_LLM_PROJECT"] = "CallerProject",
            ["ANTHROPIC_BASE_URL"] = "http://localhost:10746",
        });
        db.AgentTasks.Add(claimed);
        await db.SaveChangesAsync(Ct);

        (await dispatcher.TryReuseWarmAgentAsync(claimed, now, Ct))
            .ShouldBe(AgentTaskDispatcher.ReuseOutcome.Reused);
        await db.SaveChangesAsync(Ct);

        var warnings = await db.AgentTaskEvents
            .Where(e => e.AgentTaskId == claimed.Id && e.Type == AgentTaskEventType.Warning)
            .ToListAsync(Ct);
        warnings.ShouldContain(e => e.Detail != null && e.Detail.Contains("X_LLM_PROJECT"));
        warnings.ShouldContain(e => e.Detail != null && e.Detail.Contains("ANTHROPIC_BASE_URL"));
        warnings.ShouldNotContain(e => e.Detail != null && e.Detail.Contains("CallerProject"));
        warnings.ShouldNotContain(e => e.Detail != null && e.Detail.Contains("StandingProject"));
        standing.LaunchEnvJson.ShouldContain("StandingProject",
            customMessage: "a standing agent is never restamped from inherited env");
    }

    [Test]
    public async Task a_project_default_reaches_a_pool_delegate_and_resolves_a_project_scoped_key()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildDispatcherProvider(schema.ConnectionString, withApiKeys: true);
        var (dispatcher, db) = DispatcherOf(provider);
        var project = await AddProjectAsync(db, new Dictionary<string, string>
        {
            ["ANTHROPIC_BASE_URL"] = "http://proxy:8080",
            ["ANTHROPIC_API_KEY"] = "{{key:proxy-key}}",
        });
        var keyId = Guid.NewGuid();
        db.ApiKeys.Add(new ApiKey
        {
            Id = keyId,
            Name = "proxy-key",
            ProjectId = project.Id,
            Ciphertext = new ApiKeyStoreTests.FakeApiKeyProtector().Protect(keyId, "sk-from-project"),
            ProtectionVersion = "v1",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(Ct);

        var task = NewQueued(Path.GetTempPath(), AgentModelLevel.High);
        task.ProjectId = project.Id;
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "pool",
            Slug = $"pool-{Guid.NewGuid():N}"[..16],
            WorkingDirectory = task.WorkingDirectory,
            IsPoolDelegate = true,
        };
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Starting,
            Cwd = task.WorkingDirectory,
            Cols = 120,
            Rows = 30,
        };

        var spec = await dispatcher.BuildLaunchSpecAsync(
            task,
            agent,
            session,
            new AgentTaskDispatcher.DelegateProgram(AgentKind.ClaudeCode, "claude", null),
            attachedBundleKeys: null,
            Ct);

        spec.Env["ANTHROPIC_BASE_URL"].ShouldBe("http://proxy:8080");
        spec.Env["ANTHROPIC_API_KEY"].ShouldBe("{{key:proxy-key}}");

        var resolved = await provider.GetRequiredService<IServiceScopeFactory>()
            .CreateScope().ServiceProvider.GetRequiredService<ApiKeyEnvResolver>()
            .ResolveSpecAsync(spec, task.ProjectId, "pool delegate", Ct);
        resolved.Env["ANTHROPIC_API_KEY"].ShouldBe("sk-from-project");
    }

    [Test]
    public async Task a_task_with_no_ProjectId_does_not_inherit_any_project_default()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildDispatcherProvider(schema.ConnectionString, withApiKeys: true);
        var (dispatcher, db) = DispatcherOf(provider);
        await AddProjectAsync(db, new Dictionary<string, string> { ["SHOULD_NOT_LEAK"] = "nope" });

        var task = NewQueued(Path.GetTempPath(), AgentModelLevel.High);
        task.ProjectId = null;
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "pool",
            Slug = $"pool-{Guid.NewGuid():N}"[..16],
            WorkingDirectory = task.WorkingDirectory,
            IsPoolDelegate = true,
        };
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Starting,
            Cwd = task.WorkingDirectory,
            Cols = 120,
            Rows = 30,
        };

        var spec = await dispatcher.BuildLaunchSpecAsync(
            task,
            agent,
            session,
            new AgentTaskDispatcher.DelegateProgram(AgentKind.ClaudeCode, "claude", null),
            null,
            Ct);

        spec.Env.ContainsKey("SHOULD_NOT_LEAK").ShouldBeFalse();
    }

    [Test]
    public async Task the_funnel_loads_the_project_default_from_the_same_project_keys_resolve_against()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        var project = await AddProjectAsync(db, new Dictionary<string, string>
        {
            ["ANTHROPIC_BASE_URL"] = "http://from-project",
        });
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "board",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Boards.Add(board);
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "standing",
            Slug = $"stand-{Guid.NewGuid():N}"[..16],
            WorkingDirectory = Path.GetTempPath(),
            BoardId = board.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Agents.Add(agent);
        await db.SaveChangesAsync(Ct);

        var registry = new AgentRegistry(new OptionsMonitorStub(new AgentRegistrySettings
        {
            DefaultDefinition = "test",
            Definitions = { ["test"] = new AgentDefinition { Kind = nameof(AgentKind.Raw), Exe = "cmd.exe" } },
        }));
        var apiKeys = new ApiKeyEnvResolver(
            db, new ApiKeyStoreTests.FakeApiKeyProtector(), NullLogger<ApiKeyEnvResolver>.Instance);

        var resolved = await AgentLaunchResolution.ResolveForAgentAsync(
            agent,
            registry,
            launchResolver: null,
            new AgentLaunchOptions(Cwd: Path.GetTempPath()),
            Ct,
            apiKeys);

        resolved.Spec.Env["ANTHROPIC_BASE_URL"].ShouldBe("http://from-project");
    }

    [Test]
    public async Task ProjectService_null_leaves_stored_env_empty_clears_and_ANTIPHON_is_refused()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        var project = await AddProjectAsync(db, new Dictionary<string, string> { ["KEEP"] = "yes" });
        var service = new ProjectService(
            db,
            new DummyHttpClientFactory(),
            Options.Create(new GithubSettings()),
            NullLogger<ProjectService>.Instance);

        var unchanged = await service.UpdateAsync(
            project.Id,
            new UpdateProjectRequest(
                project.Name,
                project.GitRepositoryUrl,
                project.ConstitutionPath,
                project.GitHubIntegrationEnabled,
                project.NotificationsEnabled,
                project.LocalRepositoryPath,
                project.BaseBranch,
                DefaultLaunchEnv: null),
            Ct);
        unchanged.DefaultLaunchEnv["KEEP"].ShouldBe("yes");

        var cleared = await service.UpdateAsync(
            project.Id,
            new UpdateProjectRequest(
                project.Name,
                project.GitRepositoryUrl,
                project.ConstitutionPath,
                project.GitHubIntegrationEnabled,
                project.NotificationsEnabled,
                project.LocalRepositoryPath,
                project.BaseBranch,
                DefaultLaunchEnv: new Dictionary<string, string>()),
            Ct);
        cleared.DefaultLaunchEnv.ShouldBeEmpty();

        var ex = await Should.ThrowAsync<ValidationException>(() => service.UpdateAsync(
            project.Id,
            new UpdateProjectRequest(
                project.Name,
                project.GitRepositoryUrl,
                project.ConstitutionPath,
                project.GitHubIntegrationEnabled,
                project.NotificationsEnabled,
                project.LocalRepositoryPath,
                project.BaseBranch,
                DefaultLaunchEnv: new Dictionary<string, string>
                {
                    ["ANTIPHON_SESSION_ID"] = "nope",
                }),
            Ct));
        ex.StatusCode.ShouldBe(422);
        ex.Errors.Values.SelectMany(e => e).ShouldContain(e => e.Contains("ANTIPHON_SESSION_ID"));
    }

    private static AgentTask NewQueued(string directory, AgentModelLevel level)
    {
        var id = Guid.NewGuid();
        return new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "queued",
            Goal = "queued",
            Role = AgentTaskRole.Docs,
            ModelLevel = level,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            Status = AgentTaskStatus.Queued,
            CreatedAt = DateTime.UtcNow,
        };
    }

    private static async Task<Project> AddProjectAsync(
        AppDbContext db, IReadOnlyDictionary<string, string>? defaults = null)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"env-proj-{Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            BaseBranch = "main",
            DefaultLaunchEnvJson = AgentLaunchEnv.Serialize(defaults),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync(Ct);
        return project;
    }

    private static AppDbContext NewDb(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private static AgentTask NewParentOrchestrator(string directory, Guid? projectId)
    {
        var parent = new AgentTask
        {
            Id = Guid.NewGuid(),
            Title = "parent",
            Goal = "parent",
            Kind = AgentTaskKind.Orchestrator,
            WorkingDirectory = directory,
            ProjectId = projectId,
            CreatedAt = DateTime.UtcNow,
        };
        parent.RootTaskId = parent.Id;
        return parent;
    }

    private static ApiKeyEnvResolver ApiKeys(AppDbContext db) => new(
        db, new ApiKeyStoreTests.FakeApiKeyProtector(), NullLogger<ApiKeyEnvResolver>.Instance);

    private static AgentTaskService CreateTaskService(
        AppDbContext db,
        IReadOnlyList<string> allowedRoots,
        ApiKeyEnvResolver? apiKeyEnvResolver = null) =>
        new(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(new DelegationSettings { AllowedRoots = [.. allowedRoots] }),
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance,
            apiKeyEnvResolver: apiKeyEnvResolver);

    private static ServiceProvider BuildDispatcherProvider(string connectionString, bool withApiKeys = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings { MaxConcurrentTasks = 512 }));
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
        services.AddSingleton(Options.Create(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-env-wt"),
        }));
        services.AddSingleton<IWorktreeManager, Antiphon.Server.Infrastructure.Git.WorktreeManager>();
        services.AddSingleton<IGitService, Antiphon.Server.Infrastructure.Git.GitService>();
        // CARD-0230: DelegationWorktreeService now takes GitWorkspaceService (c4d7e0d).
        services.AddSingleton<GitWorkspaceService>();
        services.AddScoped<DelegationWorktreeService>();
        services.AddScoped<AgentTaskService>();
        if (withApiKeys)
        {
            services.AddSingleton<IApiKeyProtector, ApiKeyStoreTests.FakeApiKeyProtector>();
            services.AddScoped<ApiKeyEnvResolver>();
        }

        services.AddScoped<AgentTaskDispatcher>();
        return services.BuildServiceProvider();
    }

    private static (AgentTaskDispatcher Dispatcher, AppDbContext Db) DispatcherOf(ServiceProvider provider)
    {
        var scope = provider.CreateScope();
        return (
            scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>(),
            scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private sealed class OptionsMonitorStub : IOptionsMonitor<AgentRegistrySettings>
    {
        public OptionsMonitorStub(AgentRegistrySettings value) => CurrentValue = value;

        public AgentRegistrySettings CurrentValue { get; }

        public AgentRegistrySettings Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<AgentRegistrySettings, string?> listener) => null;
    }

    private sealed class DummyHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-env-test").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
