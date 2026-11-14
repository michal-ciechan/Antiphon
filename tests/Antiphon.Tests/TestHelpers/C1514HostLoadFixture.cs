using System.Net;
using System.Text.Json;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.HostLoad;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using RunnerSettings = Antiphon.SessionRunner.HostStatsSettings;
using ServerStatsSettings = Antiphon.Server.Application.Settings.HostStatsSettings;

namespace Antiphon.Tests.TestHelpers;

/// <summary>One server and one runner loopback listener per assembly run; cloned databases/private spools per world.</summary>
public sealed class C1514HostLoadFixture : IAsyncDisposable
{
    internal PhoneHomeProcessIdentity Identity { get; } = new();
    internal Guid StoreId { get; } = Guid.NewGuid();
    private readonly Lazy<Task<PhoneHomeTestHost>> _host;
    private readonly Lazy<Task<WebApplication>> _runner;
    internal C1514HostLoadWorld Current { get; private set; } = null!;
    internal PhoneHomeTestHost Host => _host.Value.GetAwaiter().GetResult();
    public C1514HostLoadFixture() { _host = new(StartServerAsync); _runner = new(StartRunnerAsync); }
    internal async Task<C1514HostLoadWorld> WorldAsync(bool phoneHome = false, bool advertiseHistory = true)
    {
        var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        C1514HostLoadScenario.Current?.Observe("database clone ready");
        var world = new C1514HostLoadWorld(this, schema); Current = world;
        try
        {
            await _host.Value; world.Progress("server ready");
            await _runner.Value; world.Progress("runner ready");
            await world.InitializeAsync(phoneHome, advertiseHistory); return world;
        }
        catch { await world.DisposeAsync(); throw; }
    }
    private async Task<PhoneHomeTestHost> StartServerAsync() => await PhoneHomeTestHost.StartAsync(clock: new CurrentClock(this),
        configureServices: services =>
        {
            services.AddLogging(); services.AddSingleton<TimeProvider>(new CurrentClock(this));
            services.AddDbContext<AppDbContext>(o => ConfigureDb(o, Current));
            services.AddScoped<ISessionRunnerDirectory>(sp => sp.GetRequiredService<PhoneHomeRunnerDirectory>());
            ConfigureReaders(services);
        }, mapEndpoints: app =>
        {
            app.Use(async (context, next) =>
            {
                Current.Hits.Add((context.Request.Method, context.Request.Path + context.Request.QueryString,
                    context.Request.Headers["X-Antiphon-Task-Token"].ToString()));
                if (Current.Override is { } action && context.Request.Method == "GET" && context.Request.Path.StartsWithSegments("/api"))
                { await action(context); return; }
                await next(context);
            });
            app.MapHostLoadEndpoints();
            app.MapGet("/api/runner-defaults", () => new { revision = Current.DefaultRevision, globalRunnerId = Current.DefaultRunner });
            app.MapGet("/c1514/{nonce}/host-stats/history", async (string nonce, string? after, int? limit, CancellationToken ct) =>
            {
                if (nonce != Current.Id.ToString("N")) return Results.NotFound();
                return Results.Ok(await Current.Source.GetHostLoadHistoryAsync(after, limit ?? 120, ct));
            });
        });
    private void ConfigureReaders(IServiceCollection services)
    {
        services.AddSingleton<IOptions<HostLoadHistorySettings>>(new CurrentSettings(this));
        services.AddSingleton(Options.Create(new DelegationSettings { MaxConcurrentTasks = 8 }));
        services.AddSingleton<HostLoadCounterCache>(); services.AddSingleton<HostLoadShadowCache>(); services.AddSingleton<HostLoadRecommendation>();
        services.AddScoped<HostBudgetService>(); services.AddScoped<IHostLoadCapacityReader, HostLoadCapacityReader>();
        services.AddScoped<HostLoadHistoryService>(); services.AddScoped<HostLoadIngestService>();
        services.AddScoped<HostLoadTaskWindowObserver>(); services.AddScoped<HostLoadDerivedService>();
        services.AddScoped(sp => new AgentTaskService(sp.GetRequiredService<AppDbContext>(),
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            sp.GetRequiredService<IOptions<DelegationSettings>>(), new MockEventBus(), new RecordingSessionStopper(),
            sp.GetRequiredService<TimeProvider>(), NullLogger<AgentTaskService>.Instance));
    }
    private async Task<WebApplication> StartRunnerAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IHostStatsProbe>(new C1514HostProbe());
        builder.Services.AddHostStats(new ConfigurationBuilder().Build(), startSampler: false);
        builder.Services.AddScoped<Antiphon.SessionRunner.HostStatsStore>(_ => Current.Live);
        builder.Services.AddSingleton<IHostLoadHistorySource>(new CurrentHistory(this));
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/host-stats/history")
            {
                if (Current.RunnerReply is { } reply) { await reply(context); return; }
                if (Current.OldPeer) { context.Response.StatusCode = 404; return; }
            }
            await next(context);
        });
        app.MapHostStatsRoutes(); await app.StartAsync(); return app;
    }
    internal async Task<Uri> RunnerAddressAsync()
    { var app = await _runner.Value; return new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single() + "/"); }
    internal static void ConfigureDb(DbContextOptionsBuilder options, C1514HostLoadWorld world)
    {
        options.UseNpgsql(world.Schema.ConnectionString, npgsql => npgsql.SetPostgresVersion(16, 0));
        if (world.Interceptor is { } interceptor) options.AddInterceptors(interceptor);
    }
    public async ValueTask DisposeAsync()
    {
        if (_host.IsValueCreated) await (await _host.Value).DisposeAsync();
        if (_runner.IsValueCreated) { var runner = await _runner.Value; await runner.StopAsync(); await runner.DisposeAsync(); }
    }
    private sealed class CurrentClock(C1514HostLoadFixture owner) : TimeProvider { public override DateTimeOffset GetUtcNow() => owner.Current.Clock.GetUtcNow(); }
    private sealed class CurrentSettings(C1514HostLoadFixture owner) : IOptions<HostLoadHistorySettings> { public HostLoadHistorySettings Value => owner.Current.Settings; }
    private sealed class CurrentHistory(C1514HostLoadFixture owner) : IHostLoadHistorySource
    { public HostLoadHistoryPage ReadHistory(string? after, int limit) => owner.Current.ReadHistory(after, limit); }
}

internal sealed class C1514HostLoadWorld : IAsyncDisposable
{
    private readonly C1514HostLoadFixture _fixture;
    private PhoneHomeScriptedPeer? _peer;
    private readonly HttpClient _sourceHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly CancellationTokenSource _stop;
    private C1514HostLoadScenario? _scenario;
    private CancellationTokenRegistration _scenarioStop;
    private readonly List<Task> _owned = [];
    private long _completed;
    private Task? _disposal;
    public CancellationToken Token => _stop.Token;
    public Guid Id { get; } = Guid.NewGuid();
    public IsolatedTestSchema Schema { get; }
    public C1514Clock Clock { get; } = new();
    public HostLoadHistorySettings Settings { get; } = new();
    public RunnerSettings Runner { get; }
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "c1514-world-" + Guid.NewGuid().ToString("N"));
    public IInterceptor? Interceptor { get; set; }
    public ServiceProvider Services { get; private set; } = null!;
    public HostLoadCaptureService Capture { get; private set; } = null!;
    public HostLoadSpool Spool { get; private set; } = null!;
    public IHostLoadHistorySource History { get; set; } = null!;
    public int ReadLimit { get; set; } = 120;
    public HostStatsSamplerService Sampler { get; private set; } = null!;
    public Antiphon.SessionRunner.HostStatsStore Live { get; private set; } = null!;
    public ISessionRunnerClient Source { get; private set; } = null!;
    public HostLoadIngestPump Pump { get; private set; } = null!;
    public C1514HostProbe Probe { get; } = new();
    public Func<HttpContext, Task>? Override { get; set; }
    public Func<HttpContext, Task>? RunnerReply { get; set; }
    public bool OldPeer { get; set; }
    internal PhoneHomeRunnerDirectory Directory => _fixture.Host.Directory;
    internal IServiceProvider ApiServices => _fixture.Host.App.Services;
    public long DefaultRevision { get; set; } = 1;
    public string DefaultRunner { get; set; } = "grok-linux";
    public List<(string Method, string Path, string Token)> Hits { get; } = [];
    public HttpClient Http => _fixture.Host.Http;
    public Uri Api => Http.BaseAddress!;
    public long LastSequence { get; private set; }
    public int HistoryRequests => _peer?.RequestCount(PhoneHomeOperation.HostLoadHistory) ?? 0;
    public HostLoadSource? LastSource { get; private set; }
    public string ProxyUrl => new Uri(Api, "c1514/" + Id.ToString("N") + "/").ToString();
    public static DateTimeOffset T0 => new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    public C1514HostLoadWorld(C1514HostLoadFixture fixture, IsolatedTestSchema schema)
    {
        _fixture = fixture; Schema = schema;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(TUnit.Core.TestContext.Current?.Execution.CancellationToken ?? CancellationToken.None);
        AttachScenario();
        Runner = new() { HistoryPath = Path.Combine(Root, "spool"), RunnerId = "grok-linux", PhysicalHostKey = "owned-host",
            BindingRevision = 1, SourceEpoch = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), ProcessSampling = false };
        Settings.Bindings.Add(new() { RunnerId = "grok-linux", PhysicalHostKey = "owned-host", SourceEpoch = Runner.SourceEpoch.Value, Revision = 1 });
        Settings.Bindings.Add(new() { RunnerId = "desktop", PhysicalHostKey = "owned-desktop", SourceEpoch = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Revision = 1 });
    }
    public async Task InitializeAsync(bool phoneHome, bool advertiseHistory)
    {
        using var phase = TestTiming.BeginPhase(TestPhaseKind.FixtureSetup, "host-load world initialization");
        System.IO.Directory.CreateDirectory(Root);
        // Every world is an isolated database/filesystem snapshot of the same owned runner.
        // The shared production directory correctly refuses an unrelated replacement store.
        var identity = _fixture.Identity;
        _ = new HostLoadSpool(Runner.HistoryPath!, Runner, Clock, identity.BootId);
        var ledgerPath = Path.Combine(Runner.HistoryPath!, "cursor.json");
        var ledger = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(ledgerPath))!.AsObject();
        ledger["store"] = _fixture.StoreId;
        File.WriteAllText(ledgerPath, ledger.ToJsonString());
        Spool = new(Runner.HistoryPath!, Runner, Clock, identity.BootId);
        Capture = new(Options.Create(Runner), Clock, new C1514Authority(Clock), NullLogger<HostLoadCaptureService>.Instance,
            identity, () => Spool);
        await Capture.StartAsync(Token); History = Capture;
        await EventuallyAsync(() => { try { Capture.ReadHistory(null, 120); return true; } catch (IOException e) when (e.Message == "host_load_history_busy") { return false; } }, cancellationToken: Token);
        Progress("capture ready");
        Live = new(Runner, Clock);
        Sampler = new(Probe, Live, new C1514MissingCpu(), () => [], _ => null, Clock, Options.Create(Runner), NullLogger<HostStatsSamplerService>.Instance, Capture);
        var dispatcher = new PhoneHomeCommandDispatcher(new C1514Runtime(), new PhoneHomeSettings(), hostStats: Live, hostLoad: new LimitedHistory(this));
        var oldDispatcher = new PhoneHomeCommandDispatcher(new C1514Runtime(), new PhoneHomeSettings(), hostStats: Live);
        _fixture.Host.Capacity = 8;
        var capabilities = dispatcher.Capabilities();
        if (!advertiseHistory) capabilities = capabilities with { Features = capabilities.Features!.Where(f => f != RunnerCapabilityFeatures.HostLoadHistoryV1).ToArray() };
        _peer = await _fixture.Host.ConnectPeerAsync(bootId: identity.BootId, storeId: Spool.RunnerStoreId, capabilities: capabilities);
        _peer.Reply = frame => frame.Operation is PhoneHomeOperation.HostStats or PhoneHomeOperation.HostStatsSeries or PhoneHomeOperation.HostLoadHistory
            ? (OldPeer ? oldDispatcher : dispatcher).DispatchAsync(frame, CancellationToken.None).GetAwaiter().GetResult() : null;
        _fixture.Host.Directory.MarkRecovered(await _fixture.Host.WaitLiveAsync());
        Progress("peer registered");
        _sourceHttp.BaseAddress = await _fixture.RunnerAddressAsync();
        Source = phoneHome ? _fixture.Host.Directory.Resolve("grok-linux")
            : new SessionRunnerHttpClient(_sourceHttp, new NoReadFactory(), Options.Create(new Antiphon.Server.Application.Settings.SessionRunnerSettings
                { BaseUrl = _sourceHttp.BaseAddress!.ToString() }), time: Clock);
        RecreateServices();
        // Each world has a new database/pool. Warm its real connection outside the
        // production history-read deadline; the deadline is part of the witness.
        await using var readyScope = Services.CreateAsyncScope();
        var ready = readyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await ready.Database.OpenConnectionAsync(Token);
        // Use the pump's real Npgsql options and parameterized query shape. Db(false)
        // has different provider options and a literal query does not warm this plan.
        var canonical = HostLoadCounterCache.Canonical(Runner.RunnerId);
        _ = await ready.HostLoadIngestCursors.AsNoTracking().Where(c => c.RunnerId == canonical).Select(c => c.Cursor).SingleOrDefaultAsync(Token);
        Progress("database connection and cursor ready");
    }
    public void RecreateServices()
    {
        Services?.Dispose();
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton<TimeProvider>(Clock); services.AddSingleton(Options.Create(Settings));
        services.AddSingleton(Options.Create(new DelegationSettings { MaxConcurrentTasks = 8 }));
        services.AddDbContext<AppDbContext>(o => C1514HostLoadFixture.ConfigureDb(o, this));
        services.AddSingleton<ISessionRunnerDirectory>(new C1514Directory(Source, _fixture.Host.Directory));
        services.AddSingleton<HostLoadCounterCache>(); services.AddSingleton<HostLoadShadowCache>(); services.AddSingleton<HostLoadRecommendation>();
        services.AddScoped<HostBudgetService>(); services.AddScoped<IHostLoadCapacityReader, HostLoadCapacityReader>();
        services.AddScoped<HostLoadHistoryService>(); services.AddScoped<HostLoadIngestService>();
        services.AddScoped<HostLoadTaskWindowObserver>(); services.AddScoped<HostLoadDerivedService>(); services.AddSingleton<HostLoadIngestPump>();
        Services = services.BuildServiceProvider(); Pump = Services.GetRequiredService<HostLoadIngestPump>();
    }
    public AppDbContext Db(bool intercept = true)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        if (intercept) C1514HostLoadFixture.ConfigureDb(builder, this); else builder.UseNpgsql(Schema.ConnectionString);
        return new(builder.Options);
    }
    public async Task<HostLoadSample> ProduceAsync(DateTimeOffset at, double cpu = 40)
    {
        AttachScenario();
        Clock.Utc = at; Probe.Cpu = cpu; await Sampler.SampleOnceAsync(Token); LastSequence++;
        HostLoadSample? sample = null;
        // The receipt includes the fallible history read. LastAppendedAt alone
        // does not guarantee the spool's short reader lock can be acquired.
        await EventuallyAsync(() =>
        {
            if (Capture.LastAppendedAt != at) return false;
            try { sample = Capture.ReadHistory(null, 120).Samples.LastOrDefault(s => s.Source.Sequence == LastSequence); }
            catch (IOException e) when (e.Message == "host_load_history_busy") { return false; }
            return sample is not null;
        }, () => _completed, Token);
        LastSource = sample!.Source; Progress("durable sample " + LastSequence); return sample;
    }
    public async Task SeedPrefixAsync()
    {
        using var phase = TestTiming.BeginPhase(TestPhaseKind.FixtureSetup, "host-load 100 durable receipts");
        for (var i = 0; i < 100; i++) await ProduceAsync(T0.AddHours(-2).AddSeconds(i * 5));
        await PollOnceAsync("grok-linux", Token); await ProcessOneAsync(Token);
        await DeriveAsync(); Clock.Utc = T0;
    }
    public async Task TransferAsync(bool derive = true)
    {
        (await PollOnceAsync("grok-linux", Token)).ShouldBeTrue();
        await ProcessOneAsync(Token); if (derive) await DeriveAsync();
    }
    public async Task<bool> PollOnceAsync(string runner, CancellationToken ct)
    { AttachScenario(); var enqueued = await Pump.PollOnceAsync(runner, ct); if (enqueued) Progress("history page enqueued"); return enqueued; }
    public async Task<HostLoadIngestReceipt> ProcessOneAsync(CancellationToken ct)
    { AttachScenario(); var receipt = await Pump.ProcessOneAsync(ct); Progress("ingest receipt committed"); return receipt; }
    public Task<T> Own<T>(Task<T> task) { _owned.Add(task); return task; }
    public async Task WaitForAsync(Task receipt)
    {
        try { await EventuallyAsync(() => receipt.IsCompleted, () => _completed, Token, TimeSpan.FromSeconds(5)); await receipt; Progress("held command entered"); }
        catch { _stop.Cancel(); throw; }
    }
    public async Task DeriveAsync()
    {
        AttachScenario(); using var scope = Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<HostLoadDerivedService>().DeriveOnceAsync(Token); Progress("derivation completed");
    }
    public async Task RetainAsync()
    {
        AttachScenario(); using var scope = Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<HostLoadDerivedService>().RetainOnceAsync(Token); Progress("retention completed");
    }
    public async Task<JsonDocument> GetAsync(string path)
    { AttachScenario(); using var response = await Http.GetAsync(path, Token); response.EnsureSuccessStatusCode(); var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)); Progress("HTTP response received"); return result; }
    public async Task<AgentTask> StartTaskAsync(DateTimeOffset start, string? runner = null, int attempt = 1, Guid? taskId = null)
    {
        await using var db = Db();
        var task = taskId is { } id ? await db.AgentTasks.SingleAsync(t => t.Id == id) : new AgentTask
        { Id = Guid.NewGuid(), Title = "owned telemetry task", Goal = "owned", Status = AgentTaskStatus.Working,
            CreatedAt = start.AddSeconds(-20).UtcDateTime, Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, AgentKind = AgentKind.Codex,
            Workspace = WorkspaceMode.Shared, WorkingDirectory = Root, TokenHash = AgentTaskService.HashToken("owned-task-token") };
        if (taskId is null) { task.RootTaskId = task.Id; db.AgentTasks.Add(task); }
        var session = new AgentSession { Id = Guid.NewGuid(), DefinitionName = "owned", AgentKind = AgentKind.Codex,
            CreatedAt = start.UtcDateTime, StartedAt = start.UtcDateTime, LastSeenAt = start.UtcDateTime, Status = SessionStatus.Running, Cwd = Root };
        db.AgentSessions.Add(session); task.AgentSessionId = session.Id; task.RunnerId = runner ?? "grok-linux";
        task.Attempt = attempt; task.Status = AgentTaskStatus.Working; task.DispatchedAt = start.AddSeconds(-10).UtcDateTime;
        db.AgentTaskEvents.Add(new() { Id = Guid.NewGuid(), AgentTaskId = task.Id, AgentSessionId = session.Id, Type = AgentTaskEventType.Dispatched,
            At = start.AddSeconds(1).UtcDateTime });
        await db.SaveChangesAsync(); return task;
    }
    public async Task FinishTaskAsync(Guid taskId, DateTimeOffset end)
    {
        await using var db = Db(); var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status = AgentTaskStatus.Succeeded; task.CompletedAt = end.UtcDateTime;
        db.AgentTaskEvents.Add(new() { Id = Guid.NewGuid(), AgentTaskId = taskId, Type = AgentTaskEventType.Completed, At = end.UtcDateTime });
        await db.SaveChangesAsync();
    }
    public async Task RetryThroughServiceAsync(Guid taskId)
    {
        await using var db = Db(); var settings = new DelegationSettings { AllowedRoots = [Root] };
        var service = new AgentTaskService(db, new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(settings), new MockEventBus(), new RecordingSessionStopper(), Clock, NullLogger<AgentTaskService>.Instance);
        await service.RetryAsync(taskId, CancellationToken.None);
    }
    public ValueTask DisposeAsync() => new(_disposal ??= DisposeCoreAsync());
    private async Task DisposeCoreAsync()
    {
        using var phase = TestTiming.BeginPhase(TestPhaseKind.FixtureTeardown, "host-load cancel and join world");
        _stop.Cancel();
        try
        {
            foreach (var task in _owned) try { await task; } catch (OperationCanceledException) when (Token.IsCancellationRequested) { }
            if (Pump is not null) await Pump.StopAsync(CancellationToken.None);
        }
        finally
        {
            if (_peer is not null) await _peer.DisposeAsync(); Progress("peer stopped");
            if (Capture is not null) { await Capture.StopAsync(CancellationToken.None); Capture.Dispose(); }
            Sampler?.Dispose(); Services?.Dispose(); _sourceHttp.Dispose(); Progress("world services stopped");
            await Schema.DisposeAsync(); Progress("database dropped");
            if (System.IO.Directory.Exists(Root)) System.IO.Directory.Delete(Root, true);
            _scenarioStop.Dispose(); _stop.Dispose();
        }
    }
    internal void Progress(string receipt)
    { Interlocked.Increment(ref _completed); _scenario?.Observe(receipt); }
    private void AttachScenario()
    {
        if (_scenario is not null || C1514HostLoadScenario.Current is not { } scenario) return;
        _scenario = scenario; _scenarioStop = scenario.Token.Register(() => _stop.Cancel());
    }
    internal static async Task EventuallyAsync(Func<bool> predicate, Func<long>? progress = null,
        CancellationToken cancellationToken = default, TimeSpan? quiet = null)
    {
        var parent = C1514HostLoadScenario.Current;
        progress ??= () => parent?.Completed ?? 0;
        using var wait = new C1514HostLoadScenario(quiet ?? TimeSpan.FromSeconds(15), cancellationToken);
        await wait.RunAsync(async ct =>
        {
            var last = progress();
            while (!predicate())
            {
                var current = progress();
                if (current > last) { last = current; wait.Observe("receipt counter advanced"); }
                await Task.Delay(5, ct);
            }
        });
    }
    internal HostLoadHistoryPage ReadHistory(string? after, int limit) => History.ReadHistory(after, Math.Min(limit, ReadLimit));
    private sealed class LimitedHistory(C1514HostLoadWorld world) : IHostLoadHistorySource
    { public HostLoadHistoryPage ReadHistory(string? after, int limit) => world.ReadHistory(after, limit); }
    private sealed class NoReadFactory : IHttpClientFactory { public HttpClient CreateClient(string name) => throw new InvalidOperationException("History must use its typed client."); }
}

internal sealed class C1514Clock : TimeProvider
{
    public DateTimeOffset Utc { get; set; } = C1514HostLoadWorld.T0;
    public override DateTimeOffset GetUtcNow() => Utc;
}
internal sealed class C1514HostProbe : IHostStatsProbe
{
    public double Cpu { get; set; } = 40;
    public HostSample? Read() => new(default, Cpu, 24, 24, 12, 6, 2000, 1000, 100, 50, [], 10, []);
}
internal sealed class C1514Authority(TimeProvider clock) : IHostAdmissionSnapshotReader
{ public Task<HostAdmissionSnapshot> ReadAsync(CancellationToken ct) => Task.FromResult(new HostAdmissionSnapshot("owned-authority", "owned-host", 1, clock.GetUtcNow(), "known", "Lease", 4, 6, 1)); }
internal sealed class C1514MissingCpu : IProcessCpuProbe { public TimeSpan? TryGetTotalCpuTime(int pid, DateTime startedAt) => null; }
internal sealed class C1514Directory(ISessionRunnerClient source, ISessionRunnerDirectory parent) : ISessionRunnerDirectory
{
    public ISessionRunnerClient Local => source;
    public ISessionRunnerClient Resolve(string? runnerId) => source;
    public IReadOnlyList<string> KnownRunnerIds => parent.KnownRunnerIds;
    public Guid? GetLiveStoreId(string? runnerId) => parent.GetLiveStoreId(runnerId);
    public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) => parent.GetOwnerAsync(sessionId, ct);
    public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) => parent.GetBindingAsync(sessionId, ct);
    public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) => parent.GetInventoryAsync(runnerId, ct);
    public Task<RunnerDescriptor?> DescribeAsync(string? runnerId, CancellationToken ct) => parent.DescribeAsync(runnerId, ct);
    public int? DeclaredCapacity(string runnerId) => parent.DeclaredCapacity(runnerId);
}
internal sealed class C1514Runtime : IPhoneHomeRuntimeSurface
{
    public RunnerCapabilitiesDto Capabilities() => new("InboxConhost", "inbox", "owned", false, Features: [], Platform: OperatingSystem.IsWindows() ? "windows" : "linux");
    public string Health() => "Healthy"; public IReadOnlyList<RunnerSessionDto> List() => [];
    public int OwnedSessionCount => 0;
    public Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => throw new InvalidOperationException("unrelated_session_call");
    public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct) => throw new InvalidOperationException("unrelated_launch");
    public RunnerBufferDto GetBuffer(Guid sessionId) => throw new InvalidOperationException();
    public Task<RunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) => throw new InvalidOperationException();
    public RunnerTranscriptDto GetTranscript(Guid sessionId) => throw new InvalidOperationException();
    public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => throw new InvalidOperationException();
    public Task<RunnerConditionalInputResult> SendConditionalInputAsync(Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct) => throw new InvalidOperationException();
    public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => throw new InvalidOperationException();
    public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => throw new InvalidOperationException();
    public Task<RunnerKillGenerationResult> KillGenerationAsync(Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct) => throw new InvalidOperationException();
}
