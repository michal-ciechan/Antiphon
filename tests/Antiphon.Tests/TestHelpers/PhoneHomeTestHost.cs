using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Api.Middleware;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.TestHelpers;

internal sealed class PhoneHomeTestHost : IAsyncDisposable
{
    public WebApplication App { get; private set; } = null!;
    public HttpClient Http { get; private set; } = null!;
    public PhoneHomeRunnerDirectory Directory { get; private set; } = null!;
    public Uri ConnectUri { get; private set; } = null!;
    public string Secret { get; } = "test-secret-" + Guid.NewGuid().ToString("N");
    public Guid StoreId { get; } = Guid.NewGuid();
    public Guid BootId { get; } = Guid.NewGuid();
    public string AllowedRunnerId { get; } = "grok-linux";
    public RecordingLocalClient Local { get; } = new();

    public static async Task<PhoneHomeTestHost> StartAsync(
        TimeProvider? clock = null,
        string? connectionString = null,
        PhoneHomeLimits? limits = null)
    {
        var host = new PhoneHomeTestHost();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0));
        var settings = Options.Create(new PhoneHomeRunnerSettings
        {
            Enabled = true,
            AllowedRunnerId = host.AllowedRunnerId,
            StandingAgentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            HostWorkspaceRoot = @"C:\work",
            SharedSecret = host.Secret,
            LeaseSeconds = 90,
            TicketTtlSeconds = 30,
            Limits = limits ?? new PhoneHomeLimits(),
        });
        if (connectionString is not null)
        {
            builder.Services.AddDbContext<AppDbContext>(o =>
                o.UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly("Antiphon.Server");
                    npgsql.SetPostgresVersion(16, 0);
                }));
        }

        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton<ISessionRunnerClient>(host.Local);
        builder.Services.AddSingleton(sp => new PhoneHomeRunnerDirectory(
            host.Local,
            settings,
            connectionString is null ? new EmptyScopeFactory() : sp.GetRequiredService<IServiceScopeFactory>(),
            clock ?? TimeProvider.System));
        host.App = builder.Build();
        host.Directory = host.App.Services.GetRequiredService<PhoneHomeRunnerDirectory>();
        host.App.UseWebSockets();
        host.App.UseMiddleware<ExceptionMiddleware>();
        host.App.MapSessionRunnerEndpoints();
        await host.App.StartAsync();
        var url = host.App.Urls.Single();
        host.Http = new HttpClient { BaseAddress = new Uri(url) };
        var origin = new Uri(url);
        host.ConnectUri = new Uri($"ws://{origin.Authority}/api/session-runners/{host.AllowedRunnerId}/connect");
        return host;
    }

    public PhoneHomeRegistrationRequest Registration(Guid? bootId = null, Guid? storeId = null, string? runnerId = null) =>
        new(PhoneHomeProtocol.Version, runnerId ?? AllowedRunnerId, bootId ?? BootId, storeId ?? StoreId, "linux", 1, null);

    public async Task<PhoneHomeRegistrationResponse> RegisterAsync(Guid? bootId = null, Guid? storeId = null, string? secret = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, PhoneHomeProtocol.RegisterPath);
        request.Headers.TryAddWithoutValidation(PhoneHomeProtocol.SecretHeader, secret ?? Secret);
        request.Content = JsonContent.Create(Registration(bootId, storeId), options: PhoneHomeFraming.Json);
        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PhoneHomeRegistrationResponse>(PhoneHomeFraming.Json)
            ?? throw new InvalidOperationException("empty register");
    }

    public async Task<PhoneHomeLiveConnection> WaitLiveAsync(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
        while (DateTime.UtcNow < deadline)
        {
            var live = Directory.SnapshotLive();
            if (live is not null)
                return live;
            await Task.Delay(20);
        }

        throw new TimeoutException("Phone-home live connection was not accepted.");
    }

    public async Task<PhoneHomeScriptedPeer> ConnectPeerAsync(bool autoReply = true, Guid? bootId = null)
    {
        var ticket = await RegisterAsync(bootId);
        var peer = new PhoneHomeScriptedPeer { AutoReply = autoReply };
        peer.Socket.Options.SetRequestHeader(PhoneHomeProtocol.TicketHeader, ticket.Ticket);
        await peer.Socket.ConnectAsync(ConnectUri, CancellationToken.None);
        peer.Start();
        var live = await WaitLiveAsync();
        peer.Epoch = live.Epoch;
        return peer;
    }

    public async ValueTask DisposeAsync()
    {
        Http?.Dispose();
        if (App is not null)
            await App.DisposeAsync();
    }

    private sealed class EmptyScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new EmptyScope();
        private sealed class EmptyScope : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;
            public object? GetService(Type serviceType) => null;
            public void Dispose() { }
        }
    }

    internal sealed class RecordingLocalClient : ISessionRunnerClient
    {
        public List<string> Calls { get; } = [];
        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct)
        {
            Calls.Add("start");
            return Task.FromResult(new SessionRunnerSessionDto(sessionId, 1, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, 0));
        }
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct)
        {
            Calls.Add("list");
            return Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);
        }
        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct)
        {
            Calls.Add("get");
            return Task.FromResult(new SessionRunnerSessionDto(sessionId, 1, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, 0));
        }
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerBufferDto(sessionId, "", 0));
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSnapshotDto(sessionId, "", "", 0, DateTime.UtcNow));
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerTranscriptDto(sessionId, [], 0));
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
        {
            Calls.Add("input");
            return Task.CompletedTask;
        }
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct)
        {
            Calls.Add("kill");
            return Task.FromResult(new SessionRunnerSessionDto(sessionId, 1, DateTime.UtcNow, "Exited", 0, AgentExitReason.KilledByRequest, 0));
        }
        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) => Empty();
        private static async IAsyncEnumerable<SessionRunnerEvent> Empty()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}

internal sealed class PhoneHomeScriptedPeer : IAsyncDisposable
{
    public ClientWebSocket Socket { get; } = new();
    public List<PhoneHomeFrame> Incoming { get; } = [];
    public List<PhoneHomeFrame> Launches { get; } = [];
    public List<PhoneHomeFrame> Inputs { get; } = [];
    public bool AutoReply { get; set; } = true;
    public long Epoch { get; set; } = 1;
    public Func<PhoneHomeFrame, PhoneHomeFrame?>? Reply { get; set; }
    public Dictionary<Guid, RunnerTranscriptDto> Transcripts { get; } = [];
    public List<RunnerSessionDto> Sessions { get; } = [];
    public int HeldLaunches { get; set; }
    private readonly TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public void ReleaseHeld() => _held.TrySetResult();

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _loop = RunAsync(_cts.Token);
    }

    public async Task<PhoneHomeFrame> WaitForAsync(PhoneHomeOperation operation, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(3));
        while (DateTime.UtcNow < deadline)
        {
            var match = Incoming.FirstOrDefault(f => f.Kind == PhoneHomeFrameKind.Request && f.Operation == operation);
            if (match is not null)
                return match;
            await Task.Delay(20);
        }

        throw new TimeoutException($"Did not observe {operation} request.");
    }

    public async Task EmitAsync(PhoneHomeFrame frame)
    {
        await PhoneHomeFraming.WriteFrameAsync(Socket, frame, 16 * 1024 * 1024, CancellationToken.None);
    }

    public async Task EmitTranscriptAsync(RunnerTranscriptEvent entry, long epoch)
    {
        var payload = JsonSerializer.SerializeToElement(entry, PhoneHomeFraming.Json);
        await EmitAsync(new PhoneHomeFrame(
            PhoneHomeFrameKind.Event, epoch, Guid.Empty, EventName: SessionRunnerEventNames.SessionTranscript, Payload: payload));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && Socket.State == WebSocketState.Open)
            {
                var frame = await PhoneHomeFraming.ReadFrameAsync(Socket, 16 * 1024 * 1024, ct);
                if (frame is null)
                    break;
                Incoming.Add(frame);
                if (frame.Kind != PhoneHomeFrameKind.Request)
                    continue;
                if (frame.Operation == PhoneHomeOperation.Launch)
                    Launches.Add(frame);
                if (frame.Operation == PhoneHomeOperation.Input)
                    Inputs.Add(frame);
                if (!AutoReply)
                    continue;
                if (frame.Operation == PhoneHomeOperation.Launch && HeldLaunches > 0)
                {
                    HeldLaunches--;
                    await _held.Task.WaitAsync(ct);
                }

                var reply = Reply?.Invoke(frame) ?? DefaultReply(frame);
                if (reply is not null)
                    await PhoneHomeFraming.WriteFrameAsync(Socket, reply, 16 * 1024 * 1024, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // host disposing
        }
        catch (WebSocketException)
        {
            // closed
        }
    }

    private PhoneHomeFrame DefaultReply(PhoneHomeFrame request)
    {
        object payload = request.Operation switch
        {
            PhoneHomeOperation.Health => new { status = "Healthy" },
            PhoneHomeOperation.Capabilities => new RunnerCapabilitiesDto(
                "InboxConhost", "inbox", "test", false, Features: [], VerificationCustodyBackend: null),
            PhoneHomeOperation.List => Sessions,
            PhoneHomeOperation.Get => SessionFrom(request),
            PhoneHomeOperation.Launch => LaunchFrom(request),
            PhoneHomeOperation.Transcript => TranscriptFrom(request),
            PhoneHomeOperation.Buffer => new RunnerBufferDto(ReadSessionId(request), "", 0),
            PhoneHomeOperation.Snapshot => new RunnerSnapshotDto(ReadSessionId(request), "", "", 0, DateTime.UtcNow),
            PhoneHomeOperation.Input => new { ok = true },
            PhoneHomeOperation.ClearBuffer => new { ok = true },
            PhoneHomeOperation.Resize => new { ok = true },
            PhoneHomeOperation.KillGeneration => new RunnerKillGenerationResult(
                ReadSessionId(request), true, KillGenerationOutcomes.Killed, DateTime.UtcNow),
            PhoneHomeOperation.ConditionalInput => new RunnerConditionalInputResult(
                ReadSessionId(request), ConditionalInputOutcomes.Written, DateTime.UtcNow, 1),
            _ => new { ok = true },
        };
        return new PhoneHomeFrame(
            PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation,
            JsonSerializer.SerializeToElement(payload, PhoneHomeFraming.Json));
    }

    private static Guid ReadSessionId(PhoneHomeFrame request)
    {
        if (request.Payload is { } payload
            && payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("sessionId", out var id))
            return id.GetGuid();
        return Guid.Empty;
    }

    private RunnerSessionDto SessionFrom(PhoneHomeFrame request)
    {
        var id = ReadSessionId(request);
        return Sessions.FirstOrDefault(s => s.SessionId == id)
            ?? new RunnerSessionDto(id, 1, DateTime.UtcNow, "Running", null, "", 0, AcceptedStartedAt: DateTime.UtcNow);
    }

    private RunnerSessionDto LaunchFrom(PhoneHomeFrame request)
    {
        var launch = request.Payload?.Deserialize<RunnerLaunchRequest>(PhoneHomeFraming.Json);
        var started = DateTime.UtcNow;
        var dto = new RunnerSessionDto(
            launch?.SessionId ?? Guid.NewGuid(), 1, started, "Running", null, "", 0, AcceptedStartedAt: launch?.AcceptedStartedAt ?? started);
        Sessions.Add(dto);
        return dto;
    }

    private RunnerTranscriptDto TranscriptFrom(PhoneHomeFrame request)
    {
        var id = ReadSessionId(request);
        return Transcripts.TryGetValue(id, out var t) ? t : new RunnerTranscriptDto(id, [], 0);
    }

    public async ValueTask DisposeAsync()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        try { Socket.Abort(); } catch { /* ignore */ }
        Socket.Dispose();
        if (_loop is not null)
        {
            try { await _loop.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch { /* ignore */ }
        }

        _cts?.Dispose();
    }
}
