using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.ApiKeys;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0167 S3 repair 2 (Final Review 6c08cd4b R1): the PRODUCER side of the warm-pool provider
/// projection. Every path that starts a process for a pool row writes
/// <see cref="Agent.PoolProviderProjectionJson"/> from the same endpoint-map read its launch spec
/// used, so a row never carries a stamp that differs from the endpoint its process launched with.
/// The recipient is <see cref="AgentTaskDispatcher.InheritedEnvMatchesPool(Agent, AgentTask, IReadOnlyDictionary{AgentKind, ProviderEndpoint}?, IReadOnlyList{string})"/>,
/// fed the persisted row. Key names and URLs are sentinels; the key value is a sentinel string
/// that must never appear in a stamp.
/// </summary>
[Category("Integration")]
public sealed class PoolProviderProjectionStampTests
{
    private const string KeyName = "c0167-relaunch-key";
    private const string KeyValue = "sentinel-c0167-value-not-a-key";
    private static readonly ProviderEndpoint F = new("https://proxy-f-0167.local", KeyName);
    private static readonly ProviderEndpoint FPrime = new("https://proxy-fprime-0167.local", KeyName);

    [Test]
    [Timeout(120_000)]
    public async Task C0167_cold_launch_stamps_the_map_its_launch_spec_read()
    {
        // The claim transaction and the post-commit spec build used to read the project's map
        // separately. An operator change between them (here, F -> F' at the commit boundary)
        // launched the process with F' while the row said F.
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var sink = new RecordingLaunchSink();
        var projectId = await SeedProjectAsync(schema, F);
        var boundary = new MapChangeAtClaimCommit(schema.ConnectionString, projectId, FPrime);
        await using var provider = BuildHarness(schema.ConnectionString, sink, boundary);
        var taskId = await SeedQueuedTaskAsync(schema, workspace.Path, projectId);

        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);

        boundary.Fired.ShouldBeTrue("the map changed between the claim and the spec build");
        var spec = sink.Specs.ShouldHaveSingleItem();
        spec.Env["ANTHROPIC_BASE_URL"].ShouldBe(FPrime.BaseUrl);
        var agent = await LoadTaskAgentAsync(schema, taskId);
        agent.PoolProviderProjectionJson.ShouldBe(Fingerprint(FPrime));
        agent.PoolProviderProjectionJson!.ShouldNotContain(KeyValue);
    }

    [Test]
    [Timeout(120_000)]
    public async Task C0167_boot_wedge_relaunch_restamps_after_the_endpoint_is_set_changed_and_cleared()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var sink = new RecordingLaunchSink();
        var projectId = await SeedProjectAsync(schema, endpoint: null);
        await using var provider = BuildHarness(schema.ConnectionString, sink, boundary: null);
        var seeded = await SeedWedgedPoolTaskAsync(schema, workspace.Path, projectId, stamp: null);

        // Set: launched with none, the operator sets F, the relaunch starts WITH F.
        await SetEndpointAsync(schema, projectId, F);
        var oldSession = await RelaunchAsync(provider, schema, seeded.TaskId, seeded.OldSessionId);
        sink.Specs[^1].Env["ANTHROPIC_BASE_URL"].ShouldBe(F.BaseUrl);
        (await LoadAgentAsync(schema, seeded.AgentId)).PoolProviderProjectionJson.ShouldBe(Fingerprint(F));

        // Change: F -> F'.
        await SetEndpointAsync(schema, projectId, FPrime);
        oldSession = await RelaunchAsync(provider, schema, seeded.TaskId, oldSession);
        sink.Specs[^1].Env["ANTHROPIC_BASE_URL"].ShouldBe(FPrime.BaseUrl);
        (await LoadAgentAsync(schema, seeded.AgentId)).PoolProviderProjectionJson.ShouldBe(Fingerprint(FPrime));

        // Clear: the relaunch starts with nothing, and the stamp says nothing.
        await SetEndpointAsync(schema, projectId, endpoint: null);
        await RelaunchAsync(provider, schema, seeded.TaskId, oldSession);
        sink.Specs[^1].Env.ShouldNotContainKey("ANTHROPIC_BASE_URL");
        (await LoadAgentAsync(schema, seeded.AgentId)).PoolProviderProjectionJson.ShouldBeNull();
        sink.Specs.Count.ShouldBe(3);
    }

    [Test]
    [Timeout(120_000)]
    public async Task C0167_none_wedge_set_relaunch_clear_then_the_next_task_does_not_match()
    {
        // The review's sequence, producer to recipient through the persisted row: cold launch with
        // no endpoint; the boot wedges; the operator sets F; the relaunch starts with F (and F's
        // resolved key in its environment); the operator clears F. The next task on that project
        // must not be handed to that process.
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var sink = new RecordingLaunchSink();
        var projectId = await SeedProjectAsync(schema, endpoint: null);
        await using var provider = BuildHarness(schema.ConnectionString, sink, boundary: null);
        var taskId = await SeedQueuedTaskAsync(schema, workspace.Path, projectId);

        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
        sink.Specs.ShouldHaveSingleItem().Env.ShouldNotContainKey("ANTHROPIC_BASE_URL");
        var cold = await LoadTaskAgentAsync(schema, taskId);
        cold.PoolProviderProjectionJson.ShouldBeNull();

        await SetEndpointAsync(schema, projectId, F);
        Guid wedged;
        await using (var db = Db(schema))
            wedged = (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId)).AgentSessionId!.Value;
        await RelaunchAsync(provider, schema, taskId, wedged);
        var relaunchSpec = sink.Specs[^1];
        relaunchSpec.Env["ANTHROPIC_BASE_URL"].ShouldBe(F.BaseUrl);
        relaunchSpec.Env["ANTHROPIC_API_KEY"].ShouldBe(KeyValue);

        await SetEndpointAsync(schema, projectId, endpoint: null);
        var row = await LoadAgentAsync(schema, cold.Id);
        var next = new AgentTask { AgentKind = AgentKind.ClaudeCode, ProjectId = projectId, InheritedLaunchEnvJson = "{}" };
        var currentMap = await ReadMapAsync(schema, projectId);
        AgentTaskDispatcher.InheritedEnvMatchesPool(row, next, currentMap, new LlmEnvInheritanceSettings().Names)
            .ShouldBeFalse("the process still routes through F with F's key; the project no longer does");
        row.PoolProviderProjectionJson.ShouldBe(Fingerprint(F));
        row.PoolProviderProjectionJson!.ShouldNotContain(KeyValue);
    }

    private static string? Fingerprint(ProviderEndpoint endpoint) =>
        ProviderEndpointLayers.PoolFingerprint(
            AgentKind.ClaudeCode,
            new Dictionary<AgentKind, ProviderEndpoint> { [AgentKind.ClaudeCode] = endpoint },
            ProviderEndpointMode.Inherit,
            agentEndpoint: null);

    private static async Task<Guid> RelaunchAsync(
        ServiceProvider provider, IsolatedTestSchema schema, Guid taskId, Guid oldSessionId)
    {
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
                .RelaunchWedgedAsync(taskId, oldSessionId, CancellationToken.None);
        await using var db = Db(schema);
        var current = (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId)).AgentSessionId!.Value;
        current.ShouldNotBe(oldSessionId, "the relaunch attached a new session");
        return current;
    }

    private static AppDbContext Db(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private static async Task<Agent> LoadAgentAsync(IsolatedTestSchema schema, Guid agentId)
    {
        await using var db = Db(schema);
        return await db.Agents.AsNoTracking().SingleAsync(a => a.Id == agentId);
    }

    private static async Task<Agent> LoadTaskAgentAsync(IsolatedTestSchema schema, Guid taskId)
    {
        await using var db = Db(schema);
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        task.AgentId.ShouldNotBeNull();
        var agent = await db.Agents.AsNoTracking().SingleAsync(a => a.Id == task.AgentId);
        agent.IsPoolDelegate.ShouldBeTrue();
        return agent;
    }

    private static async Task<IReadOnlyDictionary<AgentKind, ProviderEndpoint>> ReadMapAsync(
        IsolatedTestSchema schema, Guid projectId)
    {
        await using var db = Db(schema);
        var json = await db.Projects.AsNoTracking().Where(p => p.Id == projectId)
            .Select(p => p.ProviderEndpointsJson).SingleAsync();
        return ProviderEndpoint.ParseMap(json);
    }

    private static async Task SetEndpointAsync(IsolatedTestSchema schema, Guid projectId, ProviderEndpoint? endpoint) =>
        await SetEndpointAsync(schema.ConnectionString, projectId, endpoint);

    private static async Task SetEndpointAsync(string connectionString, Guid projectId, ProviderEndpoint? endpoint)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        var project = await db.Projects.SingleAsync(p => p.Id == projectId);
        project.ProviderEndpointsJson = ProviderEndpoint.SerializeMap(endpoint is null
            ? null
            : new Dictionary<AgentKind, ProviderEndpoint> { [AgentKind.ClaudeCode] = endpoint });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedProjectAsync(IsolatedTestSchema schema, ProviderEndpoint? endpoint)
    {
        var now = DateTime.UtcNow;
        await using var db = Db(schema);
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"c0167-stamp-{Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            BaseBranch = "main",
            DefaultLaunchEnvJson = "{}",
            ProviderEndpointsJson = ProviderEndpoint.SerializeMap(endpoint is null
                ? null
                : new Dictionary<AgentKind, ProviderEndpoint> { [AgentKind.ClaudeCode] = endpoint }),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Projects.Add(project);
        var keyId = Guid.NewGuid();
        db.ApiKeys.Add(new ApiKey
        {
            Id = keyId,
            Name = KeyName,
            ProjectId = project.Id,
            Ciphertext = new ApiKeyStoreTests.FakeApiKeyProtector().Protect(keyId, KeyValue),
            ProtectionVersion = "v1",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return project.Id;
    }

    private static async Task<Guid> SeedQueuedTaskAsync(IsolatedTestSchema schema, string directory, Guid projectId)
    {
        var id = Guid.NewGuid();
        await using var db = Db(schema);
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "c0167 stamp",
            Goal = "launch a pool delegate",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Docs,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            ProjectId = projectId,
            Ephemeral = true,
            Status = AgentTaskStatus.Queued,
            InheritedLaunchEnvJson = "{}",
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<WedgedSeed> SeedWedgedPoolTaskAsync(
        IsolatedTestSchema schema, string directory, Guid projectId, string? stamp)
    {
        var now = DateTime.UtcNow;
        var taskId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        await using var db = Db(schema);
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            SessionBackend = SessionBackend.PtyHost,
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
            Name = "c0167-wedged-pool",
            Slug = $"c0167-{Guid.NewGuid():N}"[..16],
            WorkingDirectory = directory,
            Details = "CARD-0167 relaunch restamp.",
            Status = AgentStatus.Running,
            ModelLevel = AgentModelLevel.Medium,
            Kind = AgentKind.ClaudeCode,
            IsPoolDelegate = true,
            PoolProjectId = projectId,
            LaunchEnvJson = "{}",
            PoolProviderProjectionJson = stamp,
            PersistentSessionId = sessionId.ToString("D"),
            SessionBackend = SessionBackend.PtyHost,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            Title = "c0167 wedged",
            Goal = "relaunch with the current endpoint",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Docs,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            ProjectId = projectId,
            Status = AgentTaskStatus.Dispatched,
            AgentId = agentId,
            AgentSessionId = sessionId,
            InheritedLaunchEnvJson = "{}",
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = now,
            DispatchedAt = now,
            ConcurrencyToken = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
        return new WedgedSeed(taskId, agentId, sessionId);
    }

    private static ServiceProvider BuildHarness(
        string connectionString, RecordingLaunchSink sink, LandDeliveryBoundary? boundary)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString, n =>
        {
            n.MigrationsAssembly("Antiphon.Server");
            n.SetPostgresVersion(16, 0);
        }));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            MaxConcurrentTasks = 512,
            AllowedRoots = ["C:\\", "/"],
            BootWedgeRelaunchLimit = 5,
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
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-c0167-stamp-wt"),
        });
        services.AddSingleton<IApiKeyProtector, ApiKeyStoreTests.FakeApiKeyProtector>();
        services.AddScoped<ApiKeyEnvResolver>();
        services.AddSingleton<IAgentTaskLaunchSink>(sink);
        if (boundary is not null)
            services.AddSingleton(boundary);
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();
        return services.BuildServiceProvider();
    }

    private sealed record WedgedSeed(Guid TaskId, Guid AgentId, Guid OldSessionId);

    /// <summary>An operator edit landing between the claim commit and the spec build.</summary>
    private sealed class MapChangeAtClaimCommit(string connectionString, Guid projectId, ProviderEndpoint next)
        : LandDeliveryBoundary
    {
        public bool Fired { get; private set; }

        public override async Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary != "dispatch-warning-claim-committed" || Fired)
                return;
            Fired = true;
            await SetEndpointAsync(connectionString, projectId, next);
        }
    }

    private sealed class RecordingLaunchSink : IAgentTaskLaunchSink
    {
        public List<AgentLaunchSpec> Specs { get; } = [];

        public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec) =>
            Specs.Add(spec);
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-c0167-stamp").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
