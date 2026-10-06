using System.Net.WebSockets;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.Application;

/// <summary>Real server clients, loopback transports, route/dispatcher and runtime. The decorator
/// records the exact DTO delivered to the runtime, and can advertise an older capability set.</summary>
internal sealed class TaskParkProofWire : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "c1065-proof-wire-" + Guid.NewGuid().ToString("N"));
    private SessionRunnerRuntime _runtime = null!;
    private WebApplication? _app;
    private HttpClient? _http;
    private PhoneHomeTestHost? _host;
    private ClientWebSocket? _socket;
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _pump;
    public bool OmitSourceModes { get; set; }
    public List<TerminalSeatReleaseRequest> Received { get; } = [];
    public int ForceCalls { get; private set; }
    public ISessionRunnerClient Client { get; private set; } = null!;
    public ISessionRunnerDirectory Directory => new ProofDirectory(Client);

    public static async Task<TaskParkProofWire> CreateAsync(bool phoneHome)
    {
        var wire = new TaskParkProofWire();
        try { await wire.StartAsync(phoneHome); return wire; }
        catch { await wire.DisposeAsync(); throw; }
    }

    private async Task StartAsync(bool phoneHome)
    {
        System.IO.Directory.CreateDirectory(_root);
        _runtime = new(Options.Create(new SessionRunnerSettings { SessionLogPath = _root }), NullLogger<SessionRunnerRuntime>.Instance);
        var surface = new Surface(this, new(_runtime, RunnerBuildIdentity.Resolve()));
        if (phoneHome)
        {
            _host = await PhoneHomeTestHost.StartAsync();
            var ticket = await _host.RegisterAsync(storeId: _runtime.RunnerStoreId, capabilities: surface.Capabilities());
            _socket = new();
            _socket.Options.SetRequestHeader(PhoneHomeProtocol.TicketHeader, ticket.Ticket);
            await _socket.ConnectAsync(_host.ConnectUri, default);
            _pump = PumpAsync(new(surface, new PhoneHomeSettings { LaunchGenerationsPath = Path.Combine(_root, "generations") }));
            var live = await _host.WaitLiveAsync();
            Client = new PhoneHomeRunnerClient(live);
            await Client.ListAsync(default);
            _host.Directory.MarkRecovered(live);
            return;
        }
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_runtime);
        builder.Services.AddSingleton<IPhoneHomeRuntimeSurface>(surface);
        builder.Services.Configure<HerdrSettings>(_ => { });
        builder.Services.Configure<HostStatsSettings>(_ => { });
        _app = builder.Build();
        _app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/capabilities" && OmitSourceModes)
            { await context.Response.WriteAsJsonAsync(surface.Capabilities()); return; }
            if (context.Request.Path.Value is { } p && (p.EndsWith("/kill") || p.EndsWith("/kill-generation") || p.EndsWith("/release")))
            { ForceCalls++; throw new InvalidOperationException("Unexpected force fallback"); }
            await next(context);
        });
        _app.MapRunnerCapabilitiesRoute(RunnerBuildIdentity.Resolve());
        _app.MapTerminalSeatReleaseRoutes();
        await _app.StartAsync();
        var address = new Uri(_app.Urls.Single());
        _http = new() { BaseAddress = address, Timeout = TimeSpan.FromSeconds(10) };
        Client = new SessionRunnerHttpClient(_http, new ClientFactory(address),
            Options.Create(new Antiphon.Server.Application.Settings.SessionRunnerSettings { BaseUrl = address.ToString() }));
    }

    private async Task PumpAsync(PhoneHomeCommandDispatcher dispatcher)
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var frame = await PhoneHomeFraming.ReadFrameAsync(_socket!, 16 * 1024 * 1024, _lifetime.Token);
                if (frame is null) break;
                if (frame.Kind != PhoneHomeFrameKind.Request) continue;
                if (frame.Operation is PhoneHomeOperation.Kill or PhoneHomeOperation.KillGeneration or PhoneHomeOperation.ReleaseSlot)
                { ForceCalls++; throw new InvalidOperationException("Unexpected force fallback"); }
                var answer = await dispatcher.DispatchAsync(frame, _lifetime.Token);
                await PhoneHomeFraming.WriteFrameAsync(_socket!, answer, 16 * 1024 * 1024, _lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (WebSocketException) when (_lifetime.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();
        _socket?.Abort();
        if (_pump is not null) await _pump;
        _socket?.Dispose();
        if (_host is not null) await _host.DisposeAsync();
        _http?.Dispose();
        if (_app is not null) { await _app.StopAsync(); await _app.DisposeAsync(); }
        if (_runtime is not null) await _runtime.DisposeAsync();
        _lifetime.Dispose();
        RunnerSettlementSyncTests.SyncWorld.DeleteTree(_root);
    }

    private sealed class ClientFactory(Uri address) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new() { BaseAddress = address }; }

    private sealed class ProofDirectory(ISessionRunnerClient client) : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local => client;
        public IReadOnlyList<string> KnownRunnerIds => ["fixture"];
        public ISessionRunnerClient Resolve(string? runnerId) => client;
        public Guid? GetLiveStoreId(string? runnerId) => null;
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) => Task.FromResult<SessionRunnerOwner?>(new("fixture", Guid.Empty, "/fixture"));
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) => Task.FromResult<SessionRunnerBinding>(new SessionRunnerBinding.Remote(new("fixture", Guid.Empty, "/fixture")));
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunnerDescriptor?> DescribeAsync(string? runnerId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Surface(TaskParkProofWire wire, PhoneHomeRuntimeAdapter inner) : IPhoneHomeRuntimeSurface
    {
        public int OwnedSessionCount => inner.OwnedSessionCount;
        public RunnerCapabilitiesDto Capabilities()
        {
            var capabilities = inner.Capabilities();
            return wire.OmitSourceModes ? capabilities with { Features = capabilities.Features!.Where(f => f != RunnerCapabilityFeatures.WorkspaceParkSourceModesV1).ToArray() } : capabilities;
        }
        public Task<TerminalSeatReleaseResult> ReleaseTerminalSeatAsync(Guid sessionId, TerminalSeatReleaseRequest request, CancellationToken ct)
        { wire.Received.Add(request); return inner.ReleaseTerminalSeatAsync(sessionId, request, ct); }
        public string Health() => inner.Health();
        public IReadOnlyList<RunnerSessionDto> List() => inner.List();
        public Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => inner.GetAsync(sessionId, ct);
        public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct) => throw new NotSupportedException();
        public RunnerBufferDto GetBuffer(Guid sessionId) => inner.GetBuffer(sessionId);
        public RunnerSnapshotDto GetSnapshot(Guid sessionId) => inner.GetSnapshot(sessionId);
        public RunnerTranscriptDto GetTranscript(Guid sessionId) => inner.GetTranscript(sessionId);
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunnerConditionalInputResult> SendConditionalInputAsync(Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunnerKillGenerationResult> KillGenerationAsync(Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct)
        { wire.ForceCalls++; throw new NotSupportedException(); }
    }
}
