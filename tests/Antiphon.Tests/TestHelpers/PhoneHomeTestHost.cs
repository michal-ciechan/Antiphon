using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Api.Middleware;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Security;
using Antiphon.SessionRunner.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

    /// <summary>CARD-0672: the session seats the next registration declares. Default one seat.</summary>
    public int Capacity { get; set; } = 1;
    public RecordingLocalClient Local { get; } = new();

    /// <summary>CARD-0679 D-11: every log entry the host wrote, with its structured properties.</summary>
    public CapturingLoggerProvider Logs { get; } = new();

    /// <summary>CARD-0653: the owner-only operator token file this host's force-release routes read.</summary>
    public string OperatorTokenPath { get; } =
        Path.Combine(Path.GetTempPath(), "antiphon-operator-" + Guid.NewGuid().ToString("N"), "operator-token");

    /// <summary>When set, the next requests are seen as coming from this address.</summary>
    public IPAddress? ClientAddress { get; set; }

    public static async Task<PhoneHomeTestHost> StartAsync(
        TimeProvider? clock = null,
        string? connectionString = null,
        PhoneHomeLimits? limits = null,
        Action<DbContextOptionsBuilder>? configureDbContext = null,
        TimeSpan? shutdownTimeout = null,
        PhoneHomeRunnerSettings? configured = null)
    {
        var host = new PhoneHomeTestHost();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(host.Logs);
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0));
        if (shutdownTimeout is { } timeout)
            builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = timeout);
        var settings = Options.Create(configured ?? new PhoneHomeRunnerSettings
        {
            Enabled = true,
            AllowedRunnerId = host.AllowedRunnerId,
            StandingAgentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            HostWorkspaceRoot = @"C:\work",
            SharedSecret = host.Secret,
            LeaseSeconds = 90,
            TicketTtlSeconds = 30,
            OperatorTokenPath = host.OperatorTokenPath,
            Limits = limits ?? new PhoneHomeLimits(),
        });
        if (configured is not null)
        {
            if (string.IsNullOrWhiteSpace(settings.Value.OperatorTokenPath))
                settings.Value.OperatorTokenPath = host.OperatorTokenPath;
            settings.Value.Limits ??= limits ?? new PhoneHomeLimits();
        }
        if (connectionString is not null)
        {
            builder.Services.AddDbContext<AppDbContext>(o =>
            {
                o.UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly("Antiphon.Server");
                    npgsql.SetPostgresVersion(16, 0);
                });
                configureDbContext?.Invoke(o);
            });
        }

        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<OperatorDashboardSessions>();
        builder.Services.Configure<OperatorSettings>(_ => { });
        builder.Services.AddSingleton<ILaunchDrain, IdleLaunchDrain>();
        builder.Services.AddSingleton<OperatorShutdownCoordinator>();
        builder.Services.AddSingleton<ISessionRunnerClient>(host.Local);
        builder.Services.AddSingleton(sp => new PhoneHomeRunnerDirectory(
            host.Local,
            settings,
            connectionString is null ? new EmptyScopeFactory() : sp.GetRequiredService<IServiceScopeFactory>(),
            clock ?? TimeProvider.System,
            inventoryLogger: sp.GetRequiredService<ILogger<PhoneHomeRunnerDirectory>>()));
        host.App = builder.Build();
        host.Directory = host.App.Services.GetRequiredService<PhoneHomeRunnerDirectory>();
        host.App.UseWebSockets();
        host.App.UseMiddleware<ExceptionMiddleware>();
        host.App.Use(async (context, next) =>
        {
            if (host.ClientAddress is { } address)
                context.Connection.RemoteIpAddress = address;
            await next(context);
        });
        host.App.MapSessionRunnerEndpoints();
        host.App.MapOperatorEndpoints();
        host.App.MapVersionEndpoints();
        await host.App.StartAsync();
        var url = host.App.Urls.Single();
        host.Http = new HttpClient { BaseAddress = new Uri(url) };
        var origin = new Uri(url);
        host.ConnectUri = new Uri($"ws://{origin.Authority}/api/session-runners/{host.AllowedRunnerId}/connect");
        return host;
    }

    public PhoneHomeRegistrationRequest Registration(Guid? bootId = null, Guid? storeId = null, string? runnerId = null) =>
        new(PhoneHomeProtocol.Version, runnerId ?? AllowedRunnerId, bootId ?? BootId, storeId ?? StoreId, "linux", Capacity, null);

    public async Task<PhoneHomeRegistrationResponse> RegisterAsync(
        Guid? bootId = null, Guid? storeId = null, string? secret = null, string? runnerId = null, string? platform = null,
        RunnerCapabilitiesDto? capabilities = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, PhoneHomeProtocol.RegisterPath);
        request.Headers.TryAddWithoutValidation(PhoneHomeProtocol.SecretHeader, secret ?? Secret);
        var registration = Registration(bootId, storeId, runnerId);
        if (platform is not null)
            registration = registration with { Platform = platform };
        if (capabilities is not null)
            registration = registration with { Capabilities = capabilities };
        request.Content = JsonContent.Create(registration, options: PhoneHomeFraming.Json);
        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PhoneHomeRegistrationResponse>(PhoneHomeFraming.Json)
            ?? throw new InvalidOperationException("empty register");
    }

    public async Task<PhoneHomeLiveConnection> WaitLiveAsync(TimeSpan? timeout = null, string? runnerId = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
        while (DateTime.UtcNow < deadline)
        {
            var live = runnerId is null ? Directory.SnapshotLive() : Directory.SnapshotLive(runnerId);
            if (live is not null)
                return live;
            await Task.Delay(20);
        }

        throw new TimeoutException("Phone-home live connection was not accepted.");
    }

    public async Task<PhoneHomeScriptedPeer> ConnectPeerAsync(
        bool autoReply = true, Guid? bootId = null, string? runnerId = null, Guid? storeId = null, string? secret = null,
        string? platform = null, RunnerCapabilitiesDto? capabilities = null)
    {
        var id = runnerId ?? AllowedRunnerId;
        var ticket = await RegisterAsync(bootId, storeId, secret, id, platform, capabilities);
        var peer = new PhoneHomeScriptedPeer { AutoReply = autoReply };
        peer.Socket.Options.SetRequestHeader(PhoneHomeProtocol.TicketHeader, ticket.Ticket);
        var uri = runnerId is null
            ? ConnectUri
            : new Uri($"ws://{Http.BaseAddress!.Authority}/api/session-runners/{Uri.EscapeDataString(id)}/connect");
        await peer.Socket.ConnectAsync(uri, CancellationToken.None);
        peer.Start();
        var live = await WaitLiveAsync(runnerId: id);
        peer.Epoch = live.Epoch;
        return peer;
    }

    /// <summary>POST as the operator: with the token when <paramref name="token"/> is set.</summary>
    /// <param name="proxied">CARD-0658: shape the request as the public vhost delivers it (Caddy
    /// and Vite: Host rewritten to localhost:17202, X-Forwarded-* naming the tailnet client).</param>
    public async Task<HttpResponseMessage> PostOperatorAsync<T>(string path, T body, string? token, bool proxied = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (token is not null)
            request.Headers.TryAddWithoutValidation(OperatorTokenFile.Header, token);
        if (proxied)
        {
            request.Headers.Host = "localhost:17202";
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", "100.64.0.7");
            request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "antiphon.desktop.codeperf.net");
        }
        request.Content = JsonContent.Create(body, options: new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return await Http.SendAsync(request);
    }

    public async ValueTask DisposeAsync()
    {
        Http?.Dispose();
        if (App is not null)
            await App.DisposeAsync();
        try
        {
            System.IO.Directory.Delete(Path.GetDirectoryName(OperatorTokenPath)!, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class IdleLaunchDrain : ILaunchDrain
    {
        public Task WaitForIdleAsync(TimeSpan timeout, CancellationToken ct) => Task.CompletedTask;
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
        public RunnerCapabilitiesDto? Capabilities { get; set; }

        public Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct) =>
            Task.FromResult(Capabilities);
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

/// <summary>
/// CARD-0679 D-11: keeps each entry's level, category and structured state, so a test asserts on
/// the named properties of a log line rather than on console text.
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<CapturedLog> _entries = new();

    public IReadOnlyList<CapturedLog> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose() { }

    private sealed class CapturingLogger(
        string category, System.Collections.Concurrent.ConcurrentQueue<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (var (key, value) in pairs)
                    properties[key] = value;
            }

            entries.Enqueue(new CapturedLog(category, logLevel, formatter(state, exception), exception, properties));
        }
    }
}

internal sealed record CapturedLog(
    string Category,
    LogLevel Level,
    string Message,
    Exception? Exception,
    IReadOnlyDictionary<string, object?> Properties)
{
    public object? this[string name] => Properties.TryGetValue(name, out var value) ? value : null;
}

internal sealed class PhoneHomeScriptedPeer : IAsyncDisposable
{
    public ClientWebSocket Socket { get; } = new();

    /// <summary>CARD-0716 D-1: completed when the server's close frame arrives, with its status and description.</summary>
    public TaskCompletionSource<(WebSocketCloseStatus? Status, string? Description)> CloseObserved { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<PhoneHomeOperation, byte> _silent = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<PhoneHomeOperation, int> _requestCounts = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public void ReleaseHeld() => _held.TrySetResult();

    /// <summary>
    /// CARD-0633: never answer <paramref name="operation"/> (a silent runner for that one request
    /// kind); every other request keeps its scripted or default reply. <see cref="Speak"/> undoes it.
    /// </summary>
    public PhoneHomeScriptedPeer SilentFor(PhoneHomeOperation operation)
    {
        _silent[operation] = 0;
        return this;
    }

    public PhoneHomeScriptedPeer Speak(PhoneHomeOperation operation)
    {
        _silent.TryRemove(operation, out _);
        return this;
    }

    /// <summary>Request frames of <paramref name="operation"/> received so far (thread-safe).</summary>
    public int RequestCount(PhoneHomeOperation operation) =>
        _requestCounts.TryGetValue(operation, out var count) ? count : 0;

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
                {
                    CloseObserved.TrySetResult((Socket.CloseStatus, Socket.CloseStatusDescription));
                    try
                    {
                        if (Socket.State is WebSocketState.CloseReceived or WebSocketState.Open)
                            await Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "ack", CancellationToken.None);
                    }
                    catch (Exception ex) when (ex is WebSocketException or InvalidOperationException or ObjectDisposedException)
                    {
                        // the server already finished the handshake
                    }

                    break;
                }
                Incoming.Add(frame);
                if (frame.Kind != PhoneHomeFrameKind.Request)
                    continue;
                if (frame.Operation is PhoneHomeOperation.Launch or PhoneHomeOperation.LaunchPlatformConstrained)
                    Launches.Add(frame);
                if (frame.Operation == PhoneHomeOperation.Input)
                    Inputs.Add(frame);
                if (frame.Operation is { } op)
                    _requestCounts.AddOrUpdate(op, 1, (_, n) => n + 1);
                if (!AutoReply || (frame.Operation is { } silent && _silent.ContainsKey(silent)))
                    continue;
                if (frame.Operation is PhoneHomeOperation.Launch or PhoneHomeOperation.LaunchPlatformConstrained && HeldLaunches > 0)
                {
                    HeldLaunches--;
                    var heldFrame = frame;
                    _ = Task.Run(async () =>
                    {
                        await _held.Task.WaitAsync(CancellationToken.None);
                        var heldReply = Reply?.Invoke(heldFrame) ?? DefaultReply(heldFrame);
                        if (heldReply is not null)
                            await PhoneHomeFraming.WriteFrameAsync(Socket, heldReply, 16 * 1024 * 1024, CancellationToken.None);
                    }, ct);
                    continue;
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
            PhoneHomeOperation.Launch or PhoneHomeOperation.LaunchPlatformConstrained => LaunchFrom(request),
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
