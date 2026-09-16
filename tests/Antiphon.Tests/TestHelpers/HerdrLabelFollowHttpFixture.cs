using System.Net;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.SessionRunner.Tests;
using Antiphon.Tests.Application;
using Antiphon.Tests.AgentTui;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Real launch queue, protocol adapter, HTTP client/routes and runtime; only Herdr/provider evidence is fake.</summary>
internal sealed class HerdrLabelFollowHttpFixture : IAsyncDisposable
{
    private IsolatedTestSchema _store = null!;
    private readonly List<AppDbContext> _contexts = [];
    private ServiceProvider _wire = null!;
    private WebApplication _app = null!;
    private readonly SemaphoreSlim _monitorGate = new(1, 1);
    private bool _monitorHeld;
    private readonly List<IAgentProtocolAdapter> _adapters = [];
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "antiphon-c462-flow-" + Guid.NewGuid().ToString("N"));
    public string Logs => Path.Combine(Root, "logs");
    public FakeHerdrServer Fake { get; } = new() { EchoSendTextToScreen = true };
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);
    public HerdrLabelFollowDbFixture.RecordingBus Bus { get; } = new();
    public SessionRunnerRuntime Runtime { get; private set; } = null!;
    public HerdrClient Herdr { get; private set; } = null!;
    public ISessionRunnerClient Client { get; private set; } = null!;
    public AgentControlServiceIntegrationTests.Harness Harness { get; private set; } = null!;
    public FakeHerdrServer.WorkspaceState Workspace { get; private set; } = null!;
    public FakeHerdrServer.TabState Tab => Workspace.Tabs[0];
    public Guid AgentId { get; private set; }
    public Guid SessionId { get; private set; }
    public List<RunnerLaunchRequest> Launches { get; } = [];
    public bool DropNextGet { get; set; }
    public string? OverrideGetJson { get; set; }
    public HerdrPaneSidecar Saved => HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(Logs, SessionId))!;
    public string[] Methods => Fake.Requests.Select(r => r.GetProperty("method").GetString()!).ToArray();

    public async Task StartAsync(bool workspacePin = true)
    {
        Directory.CreateDirectory(Root);
        _store = await TestDbFixture.CreateIsolatedSchemaAsync();
        Workspace = Fake.SeedWorkspace("w1", "Old workspace"); Tab.Label = "Old";
        Fake.Start(); await Fake.WaitUntilListeningAsync();
        Herdr = new(new HerdrSettings { Enabled = true, Session = Fake.Session });
        Runtime = new(Options.Create(new Antiphon.SessionRunner.SessionRunnerSettings { SessionLogPath = Logs }),
            NullLogger<SessionRunnerRuntime>.Instance, Herdr, new DenyProcesses(), timeProvider: Clock);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders(); builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSingleton(Runtime);
        _app = builder.Build();
        _app.Use(async (context, next) =>
        {
            var monitor = context.Request.Method == "GET" && context.Request.Path.StartsWithSegments("/sessions")
                && context.Request.Path.Value!.Count(c => c == '/') == 2 && !context.Request.Headers.ContainsKey("X-C462-Reader");
            if (monitor) await _monitorGate.WaitAsync(context.RequestAborted);
            try
            {
            if (context.Request.Method == "POST" && context.Request.Path == "/sessions")
            {
                context.Request.EnableBuffering();
                Launches.Add((await context.Request.ReadFromJsonAsync<RunnerLaunchRequest>())!);
                context.Request.Body.Position = 0;
            }
            if (DropNextGet && context.Request.Method == "GET" && context.Request.Path == $"/sessions/{SessionId}")
            { DropNextGet = false; await Runtime.GetAsync(SessionId, context.RequestAborted); context.Abort(); return; }
            if (OverrideGetJson is not null && context.Request.Method == "GET" && context.Request.Path == $"/sessions/{SessionId}")
            { context.Response.ContentType = "application/json"; await context.Response.WriteAsync(OverrideGetJson); return; }
            await next(context);
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); throw; }
            finally { if (monitor) _monitorGate.Release(); }
        });
        _app.MapSessionGetRoute(); _app.MapSessionLaunchRoute();
        _app.MapGet("/sessions", () => Runtime.List());
        _app.MapGet("/capabilities", () => new RunnerCapabilitiesDto("ModernConPty", "test", "test", false,
            SessionRunnerRuntime.SupportedTranscriptFormats, SessionBackends: [SessionBackends.Herdr],
            Features: [RunnerCapabilityFeatures.HerdrNamedTabPlacement, RunnerCapabilityFeatures.SessionGenerationV1]));
        _app.MapGet("/sessions/{id:guid}/snapshot", (Guid id) => Runtime.GetSnapshot(id));
        _app.MapGet("/sessions/{id:guid}/buffer", (Guid id) => Runtime.GetBuffer(id));
        _app.MapGet("/sessions/{id:guid}/transcript", (Guid id) => Runtime.GetTranscript(id));
        _app.MapPost("/sessions/{id:guid}/input", async (Guid id, RunnerInputRequest r, CancellationToken ct) => await Runtime.SendInputAsync(id, r.Input, ct));
        _app.MapPost("/sessions/{id:guid}/clear-live-buffer", async (Guid id, CancellationToken ct) => await Runtime.ClearLiveBufferAsync(id, ct));
        _app.MapPost("/herdr/placement/check", async (HerdrPlacementCheckRequest r, CancellationToken ct) => await Runtime.CheckHerdrPlacementAsync(r, ct));
        await _app.StartAsync();
        var services = new ServiceCollection(); services.AddLogging();
        services.AddHttpClient<ISessionRunnerClient, SessionRunnerHttpClient>(http => http.DefaultRequestHeaders.Add("X-C462-Reader", "follow"));
        services.AddSingleton(Options.Create(new Antiphon.Server.Application.Settings.SessionRunnerSettings { BaseUrl = _app.Urls.Single() }));
        _wire = services.BuildServiceProvider(); Client = _wire.GetRequiredService<ISessionRunnerClient>();
        var httpFactory = _wire.GetRequiredService<IHttpClientFactory>();
        var adapterClient = new SessionRunnerHttpClient(httpFactory.CreateClient(), httpFactory,
            Options.Create(new Antiphon.Server.Application.Settings.SessionRunnerSettings { BaseUrl = _app.Urls.Single() }));
        var registry = new AgentRegistrySettings { DefaultDefinition = "claude", ClaudeReadyQuietPeriodMs = 150,
            ClaudeReadyMaxWaitMs = 5000, ClaudeReadyMinTotalWaitMs = 0, ClaudeInputProbeTimeoutMs = 3000,
            ClaudeInputProbePollIntervalMs = 50, ClaudeInputProbeClearTimeoutMs = 1000, ClaudeInputProbeRetypeIntervalMs = 2000,
            ClaudeTrustPromptSettleMs = 200, Definitions = { ["claude"] = new() { Kind = "ClaudeCode", Exe = Path.Combine(AppContext.BaseDirectory, "fakeclaude", "fakeclaude.exe") } } };
        Harness = AgentControlServiceIntegrationTests.BuildHarness(Root, [], defaultKind: "ClaudeCode", connectionString: _store.ConnectionString,
            configureServices: s => {
                s.AddSingleton<IOptions<AgentRegistrySettings>>(Options.Create(registry));
                s.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(new OptionsMonitorStub<AgentRegistrySettings>(registry));
                s.AddSingleton(Client); s.AddSingleton(Options.Create(new SupervisionSettings()));
                s.AddSingleton<IAgentProtocolAdapterFactory>(sp => new RecordingFactory(new AgentProtocolAdapterFactory(sp.GetRequiredService<IOptions<AgentRegistrySettings>>(), adapterClient, sp.GetRequiredService<IOptions<SupervisionSettings>>()), _adapters));
            });
        var agent = await Harness.AgentService.CreateAsync(new("follow", Root, SessionBackend: SessionBackend.Herdr,
            HerdrTabLabel: "Old", HerdrWorkspaceLabel: workspacePin ? "Old workspace" : null, RemoteControlEnabled: false), CancellationToken.None);
        AgentId = agent.Id;
        if (!workspacePin)
        {
            // A tab-only pin uses the project's effective workspace label, not an explicit pin.
            await using var db = Open(); var row = await db.Agents.SingleAsync(a => a.Id == AgentId);
            var session = new Antiphon.Server.Domain.Entities.AgentSession { StandingAgentId = AgentId, SessionBackend = SessionBackend.Herdr, Cwd = Root };
            Workspace.Label = (await new HerdrLaunchContextResolver(db).ResolveAsync(session, row, "follow", CancellationToken.None)).WorkspaceLabel;
        }
        await LaunchAsync();
    }

    public async Task LaunchAsync()
    {
        ResumeMonitor();
        using var scope = Harness.Provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AgentControlService>().StartAsync(AgentId, new StartAgentRequest(Fresh: true, RemoteControl: false), CancellationToken.None);
        await Harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        await using var db = Open(); var a = await db.Agents.AsNoTracking().SingleAsync(a => a.Id == AgentId);
        SessionId = Guid.Parse(a.PersistentSessionId!);
        var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == SessionId);
        session.Status.ShouldBe(SessionStatus.Running, session.FailureReason ?? "real HTTP launch must complete");
        SessionGeneration.Equal(Saved.AcceptedStartedAt, session.StartedAt).ShouldBeTrue();
        // Park only the already-running adapter exit monitor at its HTTP request boundary.
        // Label tests then drive the same production GET route with deterministic request order.
        await _monitorGate.WaitAsync().WaitAsync(TimeSpan.FromSeconds(2)); _monitorHeld = true;
        await Client.GetAsync(SessionId, CancellationToken.None);
        Clock.Advance(TimeSpan.FromHours(1));
    }
    public void ResumeMonitor() { if (_monitorHeld) { _monitorHeld = false; _monitorGate.Release(); } }
    public AppDbContext Open() => new(TestDbFixture.CreateDbContextOptions(_store.ConnectionString));
    public HerdrLabelFollowService Service()
    {
        var db = Open(); _contexts.Add(db);
        return new(db, Client, Bus, Clock, Options.Create(new HerdrLabelFollowSettings()), NullLogger<HerdrLabelFollowService>.Instance);
    }
    public async Task<Antiphon.Server.Domain.Entities.Agent> ReadAsync()
    { await using var db = Open(); return await db.Agents.AsNoTracking().SingleAsync(a => a.Id == AgentId); }
    public void AssertReadOnly(int start)
    { var allowed = new[] { "pane.get", "pane.process_info", "pane.read", "tab.get", "workspace.get", "tab.list", "pane.list", "workspace.list", "ping", "events.subscribe" }; Methods.Skip(start).ShouldAllBe(m => allowed.Contains(m)); }
    public async ValueTask DisposeAsync()
    {
        OverrideGetJson = null; DropNextGet = false;
        if (Runtime is not null)
        {
            foreach (var pane in Runtime.LiveHerdrPanes())
            { Fake.SetPaneProcessInfo(pane.PaneId, 4242); Fake.ClearDetectedAgent(pane.PaneId); }
            ResumeMonitor();
            foreach (var adapter in _adapters) await adapter.Exited.WaitAsync(TimeSpan.FromSeconds(2));
        }
        if (Harness is not null) await Harness.DisposeAsync();
        if (_app is not null) { await _app.StopAsync(); await _app.DisposeAsync(); }
        if (_wire is not null) await _wire.DisposeAsync();
        if (Runtime is not null) await Runtime.DisposeAsync();
        await Fake.DisposeAsync(); foreach (var db in _contexts) await db.DisposeAsync();
        if (_store is not null) await _store.DisposeAsync();
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
    }
    private sealed class DenyProcesses : IProcessLivenessProbe
    { public bool IsAlive(int pid, DateTime at) => false; public DateTime? TryGetStartTimeUtc(int pid) => null; public string? TryGetProcessName(int pid) => "pwsh"; }
    private sealed class RecordingFactory(IAgentProtocolAdapterFactory inner, List<IAgentProtocolAdapter> created) : IAgentProtocolAdapterFactory
    { public IAgentProtocolAdapter Create(AgentKind kind) { var adapter = inner.Create(kind); created.Add(adapter); return adapter; } }
}
