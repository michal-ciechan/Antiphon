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

    private static AgentTaskService CreateTaskService(AppDbContext db, IReadOnlyList<string> allowedRoots) =>
        new(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(new DelegationSettings { AllowedRoots = [.. allowedRoots] }),
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance);

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
