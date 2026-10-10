using System.Data.Common;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Api.Middleware;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0505 fixtures. F-S is the fixed clock and the bound DelegationSettings seed.
/// F-H is a loopback host using the production exception middleware.
/// </summary>
public static class DispatchConcurrencyTestHost
{
    public static readonly DateTimeOffset SeedInstant = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    public static DelegationSettings BoundSettings()
    {
        var settings = new DelegationSettings
        {
            MaxOpenTasks = 9,
            MaxConcurrentTasks = 2,
        };
        settings.RolePolicy["Code"].RecommendedInFlight = 5;
        settings.RolePolicy["Review"].RecommendedInFlight = 4;
        settings.RolePolicy["Plan"].RecommendedInFlight = 3;
        return settings;
    }

    /// <summary>F-D settings. Host capacity stays above the project cap so the project gate is what holds.</summary>
    public static DelegationSettings DispatchSettings()
    {
        var settings = BoundSettings();
        settings.MaxConcurrentTasks = 32;
        settings.PoolReservedForCallerMinutes = 0;
        settings.PoolIdleRetireMinutes = 600;
        settings.PoolMaxIdlePerDirectory = 8;
        settings.MaxDepth = 5;
        settings.MaxTasksPerRoot = 100;
        settings.MaxCostUsdPerRoot = 1000;
        return settings;
    }

    public static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static PutDispatchConcurrencyRequest Put(
        long expectedRevision, string overrides, string reason, string provenance = "Human", long? expectedGlobalRevision = null) =>
        new(expectedRevision, expectedGlobalRevision, Json(overrides), reason, provenance);
}

/// <summary>
/// Hold aging jumps this clock by hand. Elapsed wall time still moves
/// <see cref="GetUtcNow"/>, and timers stay real, so a delivery confirm
/// loop can finish. Settings tests keep a frozen <see cref="FakeTimeProvider"/>.
/// </summary>
public sealed class DispatchTestClock : TimeProvider
{
    private readonly DateTimeOffset _origin;
    private readonly long _originTimestamp;
    private long _advancedTicks;

    public DispatchTestClock(DateTimeOffset origin)
    {
        _origin = origin;
        _originTimestamp = TimeProvider.System.GetTimestamp();
    }

    public void Advance(TimeSpan by) => Interlocked.Add(ref _advancedTicks, by.Ticks);

    public override DateTimeOffset GetUtcNow() =>
        _origin
        + TimeProvider.System.GetElapsedTime(_originTimestamp)
        + TimeSpan.FromTicks(Interlocked.Read(ref _advancedTicks));
}

public sealed class DispatchConcurrencyShop : IAsyncDisposable
{
    private readonly IsolatedTestSchema _schema;

    public DispatchConcurrencyShop(IsolatedTestSchema schema, DelegationSettings settings, TimeProvider clock, MockEventBus bus)
    {
        _schema = schema;
        Settings = settings;
        Clock = clock;
        Bus = bus;
    }

    public DelegationSettings Settings { get; }
    public TimeProvider Clock { get; }
    public MockEventBus Bus { get; }
    public Guid ProjectP { get; private set; }
    public Guid ProjectQ { get; private set; }
    public string ConnectionString => _schema.ConnectionString;

    public void Advance(TimeSpan by)
    {
        switch (Clock)
        {
            case DispatchTestClock stepping:
                stepping.Advance(by);
                return;
            case FakeTimeProvider frozen:
                frozen.Advance(by);
                return;
            default:
                throw new InvalidOperationException("This shop clock cannot advance.");
        }
    }

    public static async Task<DispatchConcurrencyShop> Open(DelegationSettings? settings = null, TimeProvider? clock = null)
    {
        var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var shop = new DispatchConcurrencyShop(
            schema, settings ?? DispatchConcurrencyTestHost.BoundSettings(),
            clock ?? new FakeTimeProvider(DispatchConcurrencyTestHost.SeedInstant), new MockEventBus());
        await shop.SeedProjectsAsync();
        return shop;
    }

    public AppDbContext Db(params IInterceptor[] interceptors) =>
        new(TestDbFixture.CreateDbContextOptions(ConnectionString, interceptors));

    public DispatchConcurrencySettingsService Service(
        AppDbContext db, DelegationSettings? settings = null, IEventBus? bus = null) =>
        new(db, Options.Create(settings ?? Settings), Clock, bus ?? Bus);

    public async Task<DispatchConcurrencyGlobalDto> ReadGlobalAsync()
    {
        await using var db = Db();
        return await Service(db).GetGlobalAsync(CancellationToken.None);
    }

    public async Task<DispatchConcurrencyProjectDto> ReadProjectAsync(Guid projectId)
    {
        await using var db = Db();
        return await Service(db).GetProjectAsync(projectId, CancellationToken.None);
    }

    public async Task<DispatchConcurrencyGlobalDto> PutGlobalAsync(long revision, string overrides, string reason, string provenance = "Human")
    {
        await using var db = Db();
        return await Service(db).PutGlobalAsync(
            DispatchConcurrencyTestHost.Put(revision, overrides, reason, provenance), null, CancellationToken.None);
    }

    public async Task<DispatchConcurrencyProjectDto> PutProjectAsync(
        Guid projectId, long revision, long globalRevision, string overrides, string reason, string provenance = "Human", Guid? caller = null)
    {
        await using var db = Db();
        return await Service(db).PutProjectAsync(
            projectId,
            DispatchConcurrencyTestHost.Put(revision, overrides, reason, provenance, globalRevision),
            caller,
            CancellationToken.None);
    }

    public async ValueTask DisposeAsync() => await _schema.DisposeAsync();

    private async Task SeedProjectsAsync()
    {
        var now = Clock.GetUtcNow().UtcDateTime;
        ProjectP = Guid.NewGuid();
        ProjectQ = Guid.NewGuid();
        await using var db = Db();
        db.Projects.AddRange(
            Project(ProjectP, "P", now),
            Project(ProjectQ, "Q", now));
        db.HostBudgets.Add(new HostBudget
        {
            HostId = "desktop",
            MaxInFlight = 0,
            Reason = "frozen",
            UpdatedAt = now,
            Revision = 3,
        });
        db.RunnerRoutingSettings.Add(new RunnerRoutingSettings
        {
            Id = RunnerRoutingSettings.SingletonKey,
            Revision = 4,
            GlobalRunnerId = "server2",
            UpdatedAt = now,
            LastReason = "frozen",
            LastProvenance = "Human",
        });
        await db.SaveChangesAsync();
    }

    private static Project Project(Guid id, string name, DateTime now) => new()
    {
        Id = id,
        Name = name,
        GitRepositoryUrl = $"https://example.test/{name}.git",
        CreatedAt = now,
        UpdatedAt = now,
    };
}

public sealed class DispatchConcurrencyWireHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private DispatchConcurrencyWireHost(WebApplication app, HttpClient client, DispatchConcurrencyShop shop)
    {
        _app = app;
        Client = client;
        Shop = shop;
    }

    public HttpClient Client { get; }
    public DispatchConcurrencyShop Shop { get; }

    public static async Task<DispatchConcurrencyWireHost> Start(
        DispatchConcurrencyShop shop, params IInterceptor[] interceptors)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSingleton(TestDbFixture.CreateDbContextOptions(shop.ConnectionString, interceptors));
        builder.Services.AddScoped(sp => new AppDbContext(sp.GetRequiredService<DbContextOptions<AppDbContext>>()));
        builder.Services.AddSingleton<TimeProvider>(shop.Clock);
        builder.Services.AddSingleton<IEventBus>(shop.Bus);
        builder.Services.AddSingleton(Options.Create(shop.Settings));
        builder.Services.AddScoped<DispatchConcurrencySettingsService>();
        builder.Services.AddScoped(sp => new AgentTaskService(
            sp.GetRequiredService<AppDbContext>(),
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            sp.GetRequiredService<IOptions<DelegationSettings>>(),
            sp.GetRequiredService<IEventBus>(),
            new RecordingSessionStopper(),
            sp.GetRequiredService<TimeProvider>(),
            NullLogger<AgentTaskService>.Instance));
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        });
        var app = builder.Build();
        app.UseMiddleware<ExceptionMiddleware>();
        app.MapDispatchConcurrencyEndpoints();
        await app.StartAsync();
        var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        return new DispatchConcurrencyWireHost(app, client, shop);
    }

    public Task<HttpResponseMessage> GetAsync(string path) => Client.GetAsync(path);

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? json = null, string? token = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (json is not null)
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        if (token is not null)
            request.Headers.TryAddWithoutValidation(AgentTaskEndpoints.TokenHeader, token);
        return Client.SendAsync(request);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

/// <summary>Pauses the first settings save so another session can be observed on the advisory lock.</summary>
public sealed class PauseSettingsSaveInterceptor : SaveChangesInterceptor
{
    private int _paused;

    public TaskCompletionSource AtSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var settings = eventData.Context?.ChangeTracker.Entries<DispatchConcurrencySettings>()
            .Any(entry => entry.State is EntityState.Added or EntityState.Modified) == true;
        if (settings && Interlocked.Exchange(ref _paused, 1) == 0)
        {
            AtSave.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

/// <summary>Throws once from the commit interception, after SaveChanges has sent its inserts.</summary>
public sealed class FailFirstCommitInterceptor : DbTransactionInterceptor
{
    private int _commits;

    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        System.Data.Common.DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _commits) == 1)
            throw new InvalidOperationException("interrupted before import commit");
        return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
    }
}

public static class DispatchConcurrencyLockProbe
{
    public static async Task<bool> WaitForUngrantedAdvisoryAsync(string connectionString, TimeSpan budget)
    {
        var deadline = DateTime.UtcNow + budget;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        while (DateTime.UtcNow < deadline)
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT COUNT(*)
                FROM pg_locks lock
                JOIN pg_stat_activity activity ON activity.pid = lock.pid
                WHERE lock.locktype = 'advisory'
                  AND NOT lock.granted
                  AND activity.query LIKE '%pg_advisory_xact_lock%'
                """,
                connection);
            var waiting = (long)(await command.ExecuteScalarAsync() ?? 0L);
            if (waiting > 0)
                return true;
            await Task.Delay(25);
        }

        return false;
    }
}

/// <summary>F-D. Real dispatcher over an isolated F-S shop. Launch calls are recorded, not spawned.</summary>
public sealed class ConcurrencyDispatchWorld : IAsyncDisposable
{
    private ServiceProvider _provider;
    private IServiceScope _scope;
    private AppDbContext _db;
    private readonly string _directory;

    private ConcurrencyDispatchWorld(
        DispatchConcurrencyShop shop,
        ServiceProvider provider,
        IServiceScope scope,
        AppDbContext db,
        RecordingLaunchSink launches,
        RecordingSessionStopper stopper,
        ConcurrencyRunnerDirectory runners,
        string directory)
    {
        Shop = shop;
        _provider = provider;
        _scope = scope;
        _db = db;
        Launches = launches;
        Stopper = stopper;
        Runners = runners;
        _directory = directory;
        Dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
        Queue = provider.GetRequiredService<SessionMessageQueueService>();
        Runtime = provider.GetRequiredService<AgentSessionRuntime>();
    }

    public DispatchConcurrencyShop Shop { get; }
    public RecordingLaunchSink Launches { get; }
    public RecordingSessionStopper Stopper { get; }
    public ConcurrencyRunnerDirectory Runners { get; }
    public AgentTaskDispatcher Dispatcher { get; private set; }
    public SessionMessageQueueService Queue { get; private set; }
    public AgentSessionRuntime Runtime { get; private set; }
    public AgentTaskService Tasks => _scope.ServiceProvider.GetRequiredService<AgentTaskService>();
    public string Directory => _directory;
    public HostBudgetService Budgets => _scope.ServiceProvider.GetRequiredService<HostBudgetService>();

    public static async Task<ConcurrencyDispatchWorld> Open()
    {
        var shop = await DispatchConcurrencyShop.Open(
            DispatchConcurrencyTestHost.DispatchSettings(),
            new DispatchTestClock(DispatchConcurrencyTestHost.SeedInstant));
        var directory = System.IO.Directory.CreateTempSubdirectory("c0505-dispatch").FullName;
        var launches = new RecordingLaunchSink(shop.ConnectionString);
        var stopper = new RecordingSessionStopper();
        var runners = new ConcurrencyRunnerDirectory();
        var (provider, scope, db) = Build(shop, directory, launches, stopper, runners);
        return new ConcurrencyDispatchWorld(shop, provider, scope, db, launches, stopper, runners, directory);
    }

    private static (ServiceProvider Provider, IServiceScope Scope, AppDbContext Db) Build(
        DispatchConcurrencyShop shop,
        string directory,
        RecordingLaunchSink launches,
        RecordingSessionStopper stopper,
        ConcurrencyRunnerDirectory runners)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(shop.ConnectionString));
        services.AddSingleton<IEventBus>(shop.Bus);
        services.AddSingleton<TimeProvider>(shop.Clock);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddOptions<AgentSessionSettings>();
        services.AddSingleton(Options.Create(shop.Settings));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.GrokCredentialProbeEnabled = false;
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper>(stopper);
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddSingleton<ISessionRunnerDirectory>(runners);
        services.AddSingleton<IAgentTaskLaunchSink>(launches);
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(directory, "worktrees"),
        });
        services.AddDispatchConcurrencyAdmission();
        services.AddScoped<DelegationOpenGate>();
        services.AddScoped<HostBudgetService>();
        services.AddScoped<RemoteWorkspaceService>();
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (provider, scope, db);
    }

    public async Task InitializeAsync()
    {
        await using var db = Shop.Db();
        await Shop.Service(db).EnsureInitializedAsync(CancellationToken.None);
    }

    /// <summary>Drops the provider and opens another against the same database. The schema stays.</summary>
    public async Task RebuildAsync()
    {
        Detach();
        var shop = Shop;
        var directory = _directory;
        var launches = Launches;
        var stopper = Stopper;
        var runners = Runners;
        _scope.Dispose();
        await _provider.DisposeAsync();
        (_provider, _scope, _db) = Build(shop, directory, launches, stopper, runners);
        Dispatcher = _scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
        Queue = _provider.GetRequiredService<SessionMessageQueueService>();
        Runtime = _provider.GetRequiredService<AgentSessionRuntime>();
    }

    public void Detach() => _db.ChangeTracker.Clear();

    public Task<AgentTaskDispatcher.TickResult> TickAsync() 
    {
        Detach();
        return Dispatcher.TickAsync(CancellationToken.None);
    }

    public AgentTaskDispatcher DispatcherWith(AppDbContext db, RecordingLaunchSink? launches = null)
    {
        var options = Options.Create(Shop.Settings);
        var concurrency = Shop.Service(db);
        var gate = new DelegationOpenGate(db, options, concurrency);
        var tasks = new AgentTaskService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            options,
            Shop.Bus,
            Stopper,
            Shop.Clock,
            NullLogger<AgentTaskService>.Instance,
            openGate: gate,
            dispatchConcurrency: concurrency);
        var remote = new RemoteWorkspaceService(
            Runners,
            _provider.GetRequiredService<ILandingGit>(),
            NullLogger<RemoteWorkspaceService>.Instance,
            settings: options);
        return new AgentTaskDispatcher(
            db,
            _provider.GetRequiredService<AgentRegistry>(),
            _provider.GetRequiredService<AgentSessionLaunchQueue>(),
            Queue,
            _scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>(),
            tasks,
            Stopper,
            options,
            Shop.Bus,
            Shop.Clock,
            NullLogger<AgentTaskDispatcher>.Instance,
            runtime: Runtime,
            runners: Runners,
            taskLaunchSink: launches ?? Launches,
            hostBudgets: new HostBudgetService(db, Runners, options, Shop.Clock),
            remoteWorkspace: remote,
            dispatchWarnings: new DispatchBaseWarningIntentService(db, Shop.Clock),
            dispatchConcurrency: concurrency);
    }

    public async Task<AgentTask> InsertAsync(
        AgentTaskRole role,
        AgentTaskStatus status,
        Guid? projectId,
        WorkspaceMode workspace,
        string? runnerId = null,
        bool retained = false,
        Guid? agentId = null,
        bool ephemeral = true,
        string? worktreePath = null,
        string? remoteWorktreePath = null,
        Guid? followUpOf = null,
        DateTime? createdAt = null,
        string? title = null)
    {
        var id = Guid.NewGuid();
        var now = createdAt ?? Shop.Clock.GetUtcNow().UtcDateTime;
        // A follow-up stays in the parent run, so reuse keeps that session's context
        // and delivers the brief directly.
        var rootId = id;
        if (followUpOf is Guid parentId)
        {
            await using var lookup = Shop.Db();
            rootId = await lookup.AgentTasks.AsNoTracking()
                .Where(t => t.Id == parentId)
                .Select(t => t.RootTaskId)
                .SingleAsync();
        }
        var directory = workspace == WorkspaceMode.Worktree
            ? System.IO.Directory.CreateDirectory(Path.Combine(_directory, id.ToString("N"))).FullName
            : _directory;
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = rootId,
            Title = title ?? $"c0505-{role}-{status}",
            Goal = title ?? $"c0505-{role}-{status}-{id:N}",
            Kind = AgentTaskKind.Worker,
            Role = role,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = workspace,
            WorkingDirectory = directory,
            WorktreePath = worktreePath ?? (workspace == WorkspaceMode.Worktree ? directory : null),
            RemoteWorktreePath = remoteWorktreePath,
            Status = status,
            ProjectId = projectId,
            RunnerId = runnerId,
            AgentId = agentId,
            Ephemeral = ephemeral,
            CapacityWaitRetained = retained,
            FollowUpOfTaskId = followUpOf,
            ProgressBaselineJson = remoteWorktreePath is null ? null : "{\"schemaVersion\":1}",
            CreatedAt = now,
            DispatchedAt = status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working ? now : null,
            ConcurrencyToken = Guid.NewGuid(),
        };
        await using var db = Shop.Db();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    public async Task<(Guid AgentId, Guid SessionId)> InsertAgentAsync(Guid? projectId, bool pool, string? runnerId = null)
    {
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = Shop.Clock.GetUtcNow().UtcDateTime;
        var name = $"c{agentId:N}"[..12];
        await using var db = Shop.Db();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = _directory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
            RunnerId = runnerId,
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = name,
            Slug = name,
            WorkingDirectory = _directory,
            Details = "CARD-0505 dispatch fixture.",
            Status = pool ? AgentStatus.Idle : AgentStatus.Running,
            ModelLevel = AgentModelLevel.Medium,
            Kind = AgentKind.ClaudeCode,
            IsPoolDelegate = pool,
            PoolIdleSince = pool ? now.AddMinutes(-10) : null,
            PoolProjectId = projectId,
            PersistentSessionId = sessionId.ToString("D"),
            AlwaysOn = false,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return (agentId, sessionId);
    }

    public void BindPrompt(Guid sessionId)
    {
        var adapter = new FakeAgentProtocolAdapter { TurnCompleted = true, ReadyResult = true };
        adapter.OnSubmitted = body => PersistPromptAsync(sessionId, body);
        try
        {
            Runtime.Register(sessionId, adapter);
        }
        catch (ConflictException)
        {
            // A later matrix row reuses this session. The first registration
            // already persists each submitted prompt.
        }
    }

    public async Task<TranscriptEntry?> LatestPromptAsync(Guid sessionId)
    {
        await using var db = Shop.Db();
        return await db.TranscriptEntries.AsNoTracking()
            .Where(e => e.AgentSessionId == sessionId && e.Kind == TranscriptKinds.UserPrompt)
            .OrderByDescending(e => e.Sequence)
            .FirstOrDefaultAsync();
    }

    public async Task MarkRunningAsync(Guid sessionId)
    {
        await using var db = Shop.Db();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == sessionId);
        session.Status = SessionStatus.Running;
        await db.SaveChangesAsync();
    }

    public async Task<AgentTask> ReloadAsync(Guid id)
    {
        await using var db = Shop.Db();
        return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id);
    }

    public async Task SetStatusAsync(Guid id, AgentTaskStatus status)
    {
        await using var db = Shop.Db();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == id);
        task.Status = status;
        if (status is AgentTaskStatus.Succeeded or AgentTaskStatus.Failed or AgentTaskStatus.Canceled)
            task.CompletedAt = Shop.Clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _scope.Dispose();
        await _provider.DisposeAsync();
        await Shop.DisposeAsync();
        try
        {
            System.IO.Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private async Task PersistPromptAsync(Guid sessionId, string body)
    {
        await using var db = Shop.Db();
        var sequence = await db.TranscriptEntries.Where(e => e.AgentSessionId == sessionId)
            .Select(e => (long?)e.Sequence)
            .MaxAsync() ?? 0;
        var now = Shop.Clock.GetUtcNow().UtcDateTime;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = sequence + 1,
            Kind = TranscriptKinds.UserPrompt,
            Text = body,
            Timestamp = now,
            CreatedAt = now,
        });
        await db.SaveChangesAsync();
    }
}

public sealed class RecordingLaunchSink : IAgentTaskLaunchSink
{
    private readonly string _connectionString;

    public RecordingLaunchSink(string connectionString) => _connectionString = connectionString;

    public List<(Guid SessionId, Guid AgentId, AgentLaunchSpec Spec)> Items { get; } = [];
    public bool? KeyFreeAtLaunch { get; private set; }

    /// <summary>
    /// Backend of the in-flight claim. When set, launch observation reads that
    /// backend's transaction start: a closed claim has released the parallel
    /// key it took. The converse PUT acquires that same key once the claim commits.
    /// </summary>
    public int? ClaimBackendPid { get; set; }

    public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec)
    {
        Items.Add((sessionId, agentId, spec));
        KeyFreeAtLaunch = ClaimBackendPid is int pid
            ? ClaimTransactionClosed(pid)
            : ParallelKeyIsFree();
    }

    private bool ClaimTransactionClosed(int pid)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        using var probe = new NpgsqlCommand(
            "SELECT xact_start IS NULL FROM pg_stat_activity WHERE pid = @pid", connection);
        probe.Parameters.AddWithValue("pid", pid);
        return probe.ExecuteScalar() is true;
    }

    public bool ParallelKeyIsFree()
    {
        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        using var probe = new NpgsqlCommand(
            "SELECT pg_try_advisory_lock(hashtext('antiphon.delegation.parallel-tasks'))", connection);
        var acquired = (bool)(probe.ExecuteScalar() ?? false);
        if (!acquired)
            return false;
        using var unlock = new NpgsqlCommand(
            "SELECT pg_advisory_unlock(hashtext('antiphon.delegation.parallel-tasks'))", connection);
        unlock.ExecuteScalar();
        return true;
    }
}

public sealed class ConcurrencyRunnerDirectory : ISessionRunnerDirectory
{
    private static readonly Guid StoreId = Guid.Parse("05050000-0000-4000-8000-0000000000b2");

    public ISessionRunnerClient Local => null!;
    public ISessionRunnerClient Resolve(string? runnerId) => null!;
    public ISessionRunnerClient ResolveForNewWork(string? runnerId) => null!;
    public RunnerState? DrainState(string? runnerId) => null;
    public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult<SessionRunnerOwner?>(null);
    public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Local.Instance);
    public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
        Task.FromResult<RunnerInventory>(new RunnerInventory.Available([]));
    public IReadOnlyList<string> KnownRunnerIds => ["runner-a", "runner-b"];
    public Guid? GetLiveStoreId(string? runnerId) =>
        runnerId is "runner-a" or "runner-b" ? StoreId : null;
    public int? DeclaredCapacity(string runnerId) => 32;
}

/// <summary>Pauses the first matching save. A second match sets <see cref="Crossed"/> and does not wait.</summary>
public sealed class PauseMatchSaveInterceptor : SaveChangesInterceptor
{
    private int _hits;

    public bool Crossed { get; private set; }
    public Func<DbContext, bool> Match { get; init; } = _ => false;
    public TaskCompletionSource AtSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context && Match(context))
        {
            if (Interlocked.Increment(ref _hits) == 1)
            {
                AtSave.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            else
            {
                Crossed = true;
            }
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public static bool AddedTask(DbContext context) =>
        context.ChangeTracker.Entries<AgentTask>().Any(entry => entry.State == EntityState.Added);

    public static bool ClaimedDispatch(DbContext context) =>
        context.ChangeTracker.Entries<AgentTask>().Any(entry =>
            entry.State == EntityState.Modified
            && entry.Property(t => t.Status).OriginalValue == AgentTaskStatus.Queued
            && entry.Property(t => t.Status).CurrentValue == AgentTaskStatus.Dispatched);
}

/// <summary>Records advisory and row-lock commands, and can pause the candidate FOR UPDATE.</summary>
public sealed class DispatchCommandInterceptor : DbCommandInterceptor
{
    private int _paused;
    private bool _seenCreateKey;
    private bool _seenClaim;

    public bool LockOrderBroken { get; private set; }
    public bool Crossed { get; private set; }
    public List<string> Commands { get; } = [];
    public bool PauseClaim { get; init; }
    public TaskCompletionSource AtCommand { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default) =>
        PauseAsync(command, () => base.ReaderExecutingAsync(command, eventData, result, cancellationToken), cancellationToken);

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default) =>
        PauseAsync(command, () => base.ScalarExecutingAsync(command, eventData, result, cancellationToken), cancellationToken);

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default) =>
        PauseAsync(command, () => base.NonQueryExecutingAsync(command, eventData, result, cancellationToken), cancellationToken);

    private async ValueTask<T> PauseAsync<T>(DbCommand command, Func<ValueTask<T>> next, CancellationToken cancellationToken)
    {
        var text = Describe(command);
        lock (Commands)
            Commands.Add(text);
        if (text.Contains("max-open-tasks", StringComparison.Ordinal))
            _seenCreateKey = true;
        if (text.Contains("FOR UPDATE", StringComparison.Ordinal) && text.Contains("AgentTasks", StringComparison.Ordinal))
            _seenClaim = true;
        // PUT takes the create key, then the parallel key. Dispatch takes the candidate row lock,
        // then the parallel key. Either order is legal. Parallel before both is not.
        if (text.Contains("parallel-tasks", StringComparison.Ordinal) && !_seenCreateKey && !_seenClaim)
            LockOrderBroken = true;

        var claim = PauseClaim
            && text.Contains("FOR UPDATE", StringComparison.Ordinal)
            && text.Contains("AgentTasks", StringComparison.Ordinal);
        if (claim)
        {
            if (Interlocked.Increment(ref _paused) == 1)
            {
                AtCommand.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            else
            {
                Crossed = true;
            }
        }

        return await next();
    }

    private static string Describe(DbCommand command)
    {
        var text = command.CommandText ?? string.Empty;
        foreach (System.Data.Common.DbParameter parameter in command.Parameters)
        {
            if (parameter.Value is null or DBNull)
                continue;
            text = text + "\n" + parameter.Value;
        }

        return text;
    }
}
