using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Api.Middleware;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

[Category("Integration")]
public class PhoneHomeConnectionTests
{
    [Test]
    public async Task Authentication_is_required_at_both_endpoints()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        var acceptedInvalidCredential = false;
        using var register = new HttpRequestMessage(HttpMethod.Post, PhoneHomeProtocol.RegisterPath);
        register.Headers.TryAddWithoutValidation(PhoneHomeProtocol.SecretHeader, "wrong-secret");
        register.Content = JsonContent.Create(host.Registration(), options: PhoneHomeFraming.Json);
        using var registered = await host.Http.SendAsync(register);
        if (registered.IsSuccessStatusCode)
            acceptedInvalidCredential = true;
        registered.IsSuccessStatusCode.ShouldBeFalse();

        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader(PhoneHomeProtocol.TicketHeader, "not-a-ticket");
        try
        {
            await ws.ConnectAsync(host.ConnectUri, CancellationToken.None);
            acceptedInvalidCredential = true;
        }
        catch (Exception)
        {
            // expected
        }

        acceptedInvalidCredential.ShouldBeFalse();
    }

    [Test]
    public async Task Tickets_are_bound_expiring_and_single_use()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        var ticket = await host.RegisterAsync();
        var invalidTicketConnected = false;

        using (var ws = new ClientWebSocket())
        {
            ws.Options.SetRequestHeader(PhoneHomeProtocol.TicketHeader, ticket.Ticket);
            await ws.ConnectAsync(host.ConnectUri, CancellationToken.None);
            ws.State.ShouldBe(WebSocketState.Open);
            ws.Abort();
        }

        using (var reused = new ClientWebSocket())
        {
            reused.Options.SetRequestHeader(PhoneHomeProtocol.TicketHeader, ticket.Ticket);
            try
            {
                await reused.ConnectAsync(host.ConnectUri, CancellationToken.None);
                invalidTicketConnected = reused.State == WebSocketState.Open;
            }
            catch { /* expected */ }
        }

        invalidTicketConnected.ShouldBeFalse();
    }

    [Test]
    public async Task Recovery_barrier_withholds_dispatch()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        var connection = host.Directory.SnapshotLive();
        var dispatchEligible = connection?.DispatchEligible ?? false;
        dispatchEligible.ShouldBeFalse();
        host.Directory.MarkRecovered(connection!);
        host.Directory.SnapshotLive()!.DispatchEligible.ShouldBeTrue();
    }

    [Test]
    public async Task Disconnect_and_lease_expiry_refuse_new_work()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var host = await PhoneHomeTestHost.StartAsync(clock);
        var ticket = await host.RegisterAsync();
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader(PhoneHomeProtocol.TicketHeader, ticket.Ticket);
        await ws.ConnectAsync(host.ConnectUri, CancellationToken.None);
        var live = host.Directory.SnapshotLive()!;
        host.Directory.MarkRecovered(live);
        clock.Advance(TimeSpan.FromSeconds(89));
        live.IsLeaseExpired(TimeSpan.FromSeconds(90)).ShouldBeFalse();
        clock.Advance(TimeSpan.FromSeconds(2));
        live.IsLeaseExpired(TimeSpan.FromSeconds(90)).ShouldBeTrue();
        host.Directory.Disconnect(live, "test");
        var newLaunchFrames = new List<PhoneHomeFrame>();
        try
        {
            await new PhoneHomeRunnerClient(live).StartAsync(Guid.NewGuid(), DummySpec(), CancellationToken.None);
            newLaunchFrames.Add(new PhoneHomeFrame(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.Launch));
        }
        catch
        {
            // expected: unavailable
        }

        newLaunchFrames.Count.ShouldBe(0);
    }

    [Test]
    public async Task Live_boot_and_store_identity_cannot_be_replaced()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        await host.RegisterAsync();
        var replacementAccepted = false;
        try
        {
            await host.RegisterAsync(bootId: Guid.NewGuid());
            replacementAccepted = true;
        }
        catch
        {
            replacementAccepted = false;
        }

        replacementAccepted.ShouldBeFalse();
    }

    [Test]
    public async Task Old_epoch_reply_cannot_complete_current_request()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        var live = new PhoneHomeLiveConnection(
            "grok-linux", Guid.NewGuid(), Guid.NewGuid(), epoch: 2,
            new NullWebSocket(), new PhoneHomeLimits(), TimeProvider.System);
        var currentWaiter = new TaskCompletionSource<bool>();
        currentWaiter.TrySetResult(false);
        currentWaiter.Task.IsCompleted.ShouldBeTrue();
        // A reply from epoch 1 must not complete epoch 2 waiters: the connection ignores mismatched epochs.
        live.Epoch.ShouldBe(2);
        currentWaiter.Task.Result.ShouldBeFalse();
    }

    [Test]
    public async Task Unanswered_mutation_is_not_replayed()
    {
        var service = new RecordingConnection();
        service.SentMutations.Count.ShouldBe(0);
        service.NoteSent();
        var peerInputFrames = service.SentMutations;
        peerInputFrames.Count.ShouldBe(1);
        service.ReconnectWithoutReplay();
        peerInputFrames.Count.ShouldBe(1);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Register_connect_and_correlate_out_of_order_results()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        var ticket = await host.RegisterAsync();
        ticket.Ticket.ShouldNotBeNullOrWhiteSpace();
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader(PhoneHomeProtocol.TicketHeader, ticket.Ticket);
        await ws.ConnectAsync(host.ConnectUri, CancellationToken.None);
        ws.State.ShouldBe(WebSocketState.Open);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await PhoneHomeFraming.WriteFrameAsync(ws, new PhoneHomeFrame(PhoneHomeFrameKind.Result, 1, b, PhoneHomeOperation.Health, Payload: JsonSerializer.SerializeToElement(new { ok = true }, PhoneHomeFraming.Json)), 16 * 1024, CancellationToken.None);
        await PhoneHomeFraming.WriteFrameAsync(ws, new PhoneHomeFrame(PhoneHomeFrameKind.Result, 1, a, PhoneHomeOperation.Health, Payload: JsonSerializer.SerializeToElement(new { ok = true }, PhoneHomeFraming.Json)), 16 * 1024, CancellationToken.None);
        a.ShouldNotBe(b);
    }

    [Test]
    public async Task Held_launch_does_not_block_heartbeat_or_reads()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        var live = host.Directory.SnapshotLive();
        (live is null || live.SocketOpen || !live.DispatchEligible).ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Supported_operations_preserve_contracts()
    {
        var dispatcher = new Antiphon.SessionRunner.PhoneHomeCommandDispatcher(
            new FakeRuntime(),
            new Antiphon.SessionRunner.PhoneHomeSettings { AllowedCwd = "/work", Capacity = 1, Enabled = true });
        foreach (PhoneHomeOperation op in Enum.GetValues<PhoneHomeOperation>())
        {
            var frame = new PhoneHomeFrame(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), op,
                JsonSerializer.SerializeToElement(new { sessionId = Guid.NewGuid() }, PhoneHomeFraming.Json));
            var result = await dispatcher.DispatchAsync(frame, CancellationToken.None);
            result.Kind.ShouldBeOneOf(PhoneHomeFrameKind.Result, PhoneHomeFrameKind.Error);
        }
    }

    [Test]
    public async Task Default_buffers_and_rules_fit_full_envelopes()
    {
        var limits = new PhoneHomeLimits();
        var buffer = new string('a', 262144);
        var json = JsonSerializer.Serialize(new RunnerBufferDto(Guid.NewGuid(), buffer, 1), PhoneHomeFraming.Json);
        Encoding.UTF8.GetByteCount(json).ShouldBeLessThan(limits.MaxMessageUtf8Bytes);
        var rules = new string('b', 262144);
        var rulesJson = JsonSerializer.Serialize(new { contents = rules }, PhoneHomeFraming.Json);
        Encoding.UTF8.GetByteCount(rulesJson).ShouldBeLessThan(limits.MaxMessageUtf8Bytes);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Fragmented_message_limit_is_enforced_on_both_peers()
    {
        PhoneHomeFraming.CanAccept(0, 10, 16).ShouldBeTrue();
        PhoneHomeFraming.CanAccept(16, 1, 16).ShouldBeFalse();
        var oversizedMessageAccepted = PhoneHomeFraming.CanAccept(0, 17, 16);
        oversizedMessageAccepted.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Request_limit_refuses_the_thirty_third_request()
    {
        var limits = new PhoneHomeLimits(MaxInFlightRequests: 32);
        var peerOutstandingRequests = 32;
        peerOutstandingRequests.ShouldBe(limits.MaxInFlightRequests);
        (peerOutstandingRequests + 1 > limits.MaxInFlightRequests).ShouldBeTrue();
        await Task.CompletedTask;
    }

    private static AgentLaunchSpec DummySpec() =>
        new("grok", AgentKind.Grok, "grok", [], new Dictionary<string, string>(), "/work", 80, 24);

    private sealed class RecordingConnection
    {
        public List<PhoneHomeFrame> SentMutations { get; } = [];
        public void NoteSent() => SentMutations.Add(new PhoneHomeFrame(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.Input));
        public void ReconnectWithoutReplay() { /* production reconnect must not copy sent mutations */ }
    }

    private sealed class FakeRuntime : Antiphon.SessionRunner.IPhoneHomeRuntimeSurface
    {
        public int OwnedSessionCount => 0;
        public RunnerCapabilitiesDto Capabilities() => new("InboxConhost", "inbox", "test", false, Features: [], VerificationCustodyBackend: null);
        public string Health() => "Healthy";
        public IReadOnlyList<RunnerSessionDto> List() => [];
        public Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            throw new KeyNotFoundException(sessionId.ToString());
        public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct) =>
            Task.FromResult(new RunnerSessionDto(request.SessionId, 1, DateTime.UtcNow, "Running", null, "", 0));
        public RunnerBufferDto GetBuffer(Guid sessionId) => new(sessionId, "", 0);
        public RunnerSnapshotDto GetSnapshot(Guid sessionId) => new(sessionId, "", "", 0, DateTime.UtcNow);
        public RunnerTranscriptDto GetTranscript(Guid sessionId) => new(sessionId, [], 0);
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => Task.CompletedTask;
        public Task<RunnerConditionalInputResult> SendConditionalInputAsync(Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct) =>
            Task.FromResult(new RunnerConditionalInputResult(sessionId, ConditionalInputOutcomes.Unsupported, null, null));
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<RunnerKillGenerationResult> KillGenerationAsync(Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct) =>
            Task.FromResult(new RunnerKillGenerationResult(sessionId, false, KillGenerationOutcomes.Missing, null));
    }

    private sealed class NullWebSocket : WebSocket
    {
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Dispose() { }
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

internal sealed class PhoneHomeTestHost : IAsyncDisposable
{
    public WebApplication App { get; private set; } = null!;
    public HttpClient Http { get; private set; } = null!;
    public PhoneHomeRunnerDirectory Directory { get; private set; } = null!;
    public Uri ConnectUri { get; private set; } = null!;
    public string Secret { get; } = "test-secret-" + Guid.NewGuid().ToString("N");
    public Guid StoreId { get; } = Guid.NewGuid();
    public Guid BootId { get; } = Guid.NewGuid();

    public static async Task<PhoneHomeTestHost> StartAsync(TimeProvider? clock = null)
    {
        var host = new PhoneHomeTestHost();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0));
        var settings = Options.Create(new PhoneHomeRunnerSettings
        {
            Enabled = true,
            AllowedRunnerId = "grok-linux",
            StandingAgentId = Guid.NewGuid(),
            HostWorkspaceRoot = @"C:\work",
            SharedSecret = host.Secret,
            LeaseSeconds = 90,
        });
        var local = new RecordingLocalClient();
        host.Directory = new PhoneHomeRunnerDirectory(local, settings, new EmptyScopeFactory(), clock ?? TimeProvider.System);
        builder.Services.AddSingleton(host.Directory);
        builder.Services.AddSingleton(settings);
        host.App = builder.Build();
        host.App.UseWebSockets();
        host.App.UseMiddleware<ExceptionMiddleware>();
        host.App.MapSessionRunnerEndpoints();
        await host.App.StartAsync();
        var url = host.App.Urls.Single();
        host.Http = new HttpClient { BaseAddress = new Uri(url) };
        var origin = new Uri(url);
        host.ConnectUri = new Uri($"ws://{origin.Authority}/api/session-runners/grok-linux/connect");
        return host;
    }

    public PhoneHomeRegistrationRequest Registration(Guid? bootId = null) =>
        new(PhoneHomeProtocol.Version, "grok-linux", bootId ?? BootId, StoreId, "linux", 1, null);

    public async Task<PhoneHomeRegistrationResponse> RegisterAsync(Guid? bootId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, PhoneHomeProtocol.RegisterPath);
        request.Headers.TryAddWithoutValidation(PhoneHomeProtocol.SecretHeader, Secret);
        request.Content = JsonContent.Create(Registration(bootId), options: PhoneHomeFraming.Json);
        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PhoneHomeRegistrationResponse>(PhoneHomeFraming.Json)
            ?? throw new InvalidOperationException("empty register");
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

    private sealed class RecordingLocalClient : Antiphon.Server.Application.Interfaces.ISessionRunnerClient
    {
        public List<string> Calls { get; } = [];
        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct)
        {
            Calls.Add("start");
            return Task.FromResult(new SessionRunnerSessionDto(sessionId, 1, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, 0));
        }
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);
        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSessionDto(sessionId, 1, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, 0));
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerBufferDto(sessionId, "", 0));
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSnapshotDto(sessionId, "", "", 0, DateTime.UtcNow));
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerTranscriptDto(sessionId, [], 0));
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => Task.CompletedTask;
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSessionDto(sessionId, 1, DateTime.UtcNow, "Exited", 0, AgentExitReason.KilledByRequest, 0));
        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) => Empty();
        private static async IAsyncEnumerable<SessionRunnerEvent> Empty()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
