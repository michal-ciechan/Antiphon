using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
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
        registered.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader(PhoneHomeProtocol.TicketHeader, "not-a-ticket");
        try
        {
            await ws.ConnectAsync(host.ConnectUri, CancellationToken.None);
            acceptedInvalidCredential = true;
        }
        catch (Exception)
        {
            // expected: handshake rejected before a socket is established
        }

        acceptedInvalidCredential.ShouldBeFalse();
    }

    [Test]
    public async Task Tickets_are_bound_expiring_and_single_use()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var host = await PhoneHomeTestHost.StartAsync(clock);
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
            catch
            {
                // expected
            }
        }

        invalidTicketConnected.ShouldBeFalse();

        var fresh = await host.RegisterAsync();
        clock.Advance(TimeSpan.FromSeconds(31));
        using (var expired = new ClientWebSocket())
        {
            expired.Options.SetRequestHeader(PhoneHomeProtocol.TicketHeader, fresh.Ticket);
            try
            {
                await expired.ConnectAsync(host.ConnectUri, CancellationToken.None);
                invalidTicketConnected = expired.State == WebSocketState.Open;
            }
            catch
            {
                // expected
            }
        }

        invalidTicketConnected.ShouldBeFalse();
    }

    [Test]
    public async Task Recovery_barrier_withholds_dispatch()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        var dispatchEligible = live.DispatchEligible;
        dispatchEligible.ShouldBeFalse();

        var client = new PhoneHomeRunnerClient(live);
        await Should.ThrowAsync<InvalidOperationException>(() =>
            client.StartAsync(Guid.NewGuid(), DummySpec(), CancellationToken.None));
        peer.Launches.ShouldBeEmpty();

        host.Directory.MarkRecovered(live);
        host.Directory.SnapshotLive()!.DispatchEligible.ShouldBeTrue();
        var started = await client.StartAsync(Guid.NewGuid(), DummySpec(), CancellationToken.None);
        started.Status.ShouldBe("Running");
        peer.Launches.Count.ShouldBe(1);
    }

    [Test]
    public async Task Disconnect_and_lease_expiry_refuse_new_work()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var host = await PhoneHomeTestHost.StartAsync(clock);
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        clock.Advance(TimeSpan.FromSeconds(89));
        live.IsLeaseExpired(TimeSpan.FromSeconds(90)).ShouldBeFalse();
        clock.Advance(TimeSpan.FromSeconds(2));
        live.IsLeaseExpired(TimeSpan.FromSeconds(90)).ShouldBeTrue();
        host.Directory.SnapshotLive();
        var newLaunchFrames = new List<PhoneHomeFrame>();
        try
        {
            await host.Directory.Resolve("grok-linux").StartAsync(Guid.NewGuid(), DummySpec(), CancellationToken.None);
            newLaunchFrames.AddRange(peer.Launches);
        }
        catch
        {
            // expected: unavailable after lease expiry
        }

        host.Directory.Disconnect(live, "test");
        try
        {
            await host.Directory.Resolve("grok-linux").StartAsync(Guid.NewGuid(), DummySpec(), CancellationToken.None);
            newLaunchFrames.AddRange(peer.Launches);
        }
        catch
        {
            // expected
        }

        newLaunchFrames.Count.ShouldBe(0);
        peer.Launches.ShouldBeEmpty();
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

        try
        {
            await host.RegisterAsync(storeId: Guid.NewGuid());
            replacementAccepted = true;
        }
        catch
        {
            replacementAccepted = false;
        }

        replacementAccepted.ShouldBeFalse();

        var sameBoot = await host.RegisterAsync();
        sameBoot.Ticket.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task Old_epoch_reply_cannot_complete_current_request()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync(autoReply: false);
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        var client = new PhoneHomeRunnerClient(live);
        var currentWaiter = client.GetHealthAsync(CancellationToken.None);
        var request = await peer.WaitForAsync(PhoneHomeOperation.Health);
        await peer.EmitAsync(new PhoneHomeFrame(
            PhoneHomeFrameKind.Result, live.Epoch - 1, request.RequestId, PhoneHomeOperation.Health,
            JsonSerializer.SerializeToElement(new { status = "stale" }, PhoneHomeFraming.Json)));
        await Task.Delay(100);
        currentWaiter.IsCompleted.ShouldBeFalse();
        await peer.EmitAsync(new PhoneHomeFrame(
            PhoneHomeFrameKind.Result, live.Epoch, request.RequestId, PhoneHomeOperation.Health,
            JsonSerializer.SerializeToElement(new { status = "Healthy" }, PhoneHomeFraming.Json)));
        var health = await currentWaiter.WaitAsync(TimeSpan.FromSeconds(3));
        health.ShouldNotBeNull();
    }

    // CARD-0629: a runner that never replies must not hold the caller forever. Live on 2026-09-23 a
    // silent WorkspaceMirror reply froze the serial dispatcher tick for hours, fleet-wide.
    [Test]
    public async Task Unanswered_request_times_out_instead_of_waiting_forever()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var host = await PhoneHomeTestHost.StartAsync(clock);
        await using var peer = await host.ConnectPeerAsync(autoReply: false);
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        var client = new PhoneHomeRunnerClient(live);

        var pending = client.GetHealthAsync(CancellationToken.None);
        await peer.WaitForAsync(PhoneHomeOperation.Health);
        clock.Advance(PhoneHomeLiveConnection.RequestTimeoutFor(PhoneHomeOperation.Health) - TimeSpan.FromSeconds(1));
        await Task.Delay(100);
        pending.IsCompleted.ShouldBeFalse("the request must still be waiting just inside its budget");

        clock.Advance(TimeSpan.FromSeconds(2));
        var ex = await Should.ThrowAsync<PhoneHomeTransportException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        ex.Code.ShouldBe(PhoneHomeProblemTypes.RequestTimeout);
        live.InFlight.ShouldBe(0);
    }

    [Test]
    public async Task Unanswered_mutation_is_not_replayed()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync(autoReply: false);
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        var client = new PhoneHomeRunnerClient(live);
        var sent = client.SendInputAsync(Guid.NewGuid(), "hello-phone-home", CancellationToken.None);
        var first = await peer.WaitForAsync(PhoneHomeOperation.Input);
        host.Directory.Disconnect(live, "drop");
        await Should.ThrowAsync<Exception>(async () => await sent.WaitAsync(TimeSpan.FromSeconds(2)));
        var peerInputFrames = peer.Inputs;
        peerInputFrames.Count.ShouldBe(1);

        await using var peer2 = await host.ConnectPeerAsync(autoReply: false);
        await Task.Delay(200);
        peer2.Inputs.ShouldBeEmpty();
        peerInputFrames.Count.ShouldBe(1);
    }

    [Test]
    public async Task Register_connect_and_correlate_out_of_order_results()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync(autoReply: false);
        var live = await host.WaitLiveAsync();
        var client = new PhoneHomeRunnerClient(live);
        var a = client.GetHealthAsync(CancellationToken.None);
        var first = await peer.WaitForAsync(PhoneHomeOperation.Health);
        var b = client.GetBufferAsync(Guid.NewGuid(), CancellationToken.None);
        var second = await peer.WaitForAsync(PhoneHomeOperation.Buffer);
        second.RequestId.ShouldNotBe(first.RequestId);
        await peer.EmitAsync(new PhoneHomeFrame(
            PhoneHomeFrameKind.Result, live.Epoch, second.RequestId, PhoneHomeOperation.Buffer,
            JsonSerializer.SerializeToElement(new RunnerBufferDto(Guid.Empty, "later", 2), PhoneHomeFraming.Json)));
        var buffer = await b.WaitAsync(TimeSpan.FromSeconds(3));
        buffer.Buffer.ShouldBe("later");
        a.IsCompleted.ShouldBeFalse();
        await peer.EmitAsync(new PhoneHomeFrame(
            PhoneHomeFrameKind.Result, live.Epoch, first.RequestId, PhoneHomeOperation.Health,
            JsonSerializer.SerializeToElement(new { status = "Healthy" }, PhoneHomeFraming.Json)));
        var health = await a.WaitAsync(TimeSpan.FromSeconds(3));
        health.ShouldNotBeNull();
    }

    [Test]
    public async Task Held_launch_does_not_block_heartbeat_or_reads()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        peer.HeldLaunches = 1;
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        var client = new PhoneHomeRunnerClient(live);
        var launch = client.StartAsync(Guid.NewGuid(), DummySpec(), CancellationToken.None);
        await peer.WaitForAsync(PhoneHomeOperation.Launch);
        var health = await client.GetHealthAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
        health.ShouldNotBeNull();
        launch.IsCompleted.ShouldBeFalse();
        peer.ReleaseHeld();
        var started = await launch.WaitAsync(TimeSpan.FromSeconds(3));
        started.Status.ShouldBe("Running");
    }

    [Test]
    public async Task Supported_operations_preserve_contracts()
    {
        var dispatcher = new Antiphon.SessionRunner.PhoneHomeCommandDispatcher(
            new FakeRuntime(),
            new Antiphon.SessionRunner.PhoneHomeSettings { AllowedCwd = "/work", Capacity = 1, Enabled = true });
        foreach (PhoneHomeOperation op in Enum.GetValues<PhoneHomeOperation>())
        {
            object body = op == PhoneHomeOperation.Launch
                ? new RunnerLaunchRequest(Guid.NewGuid(), "grok", [], new Dictionary<string, string>(), "/work", 80, 24)
                : op is PhoneHomeOperation.Input
                    ? new { sessionId = Guid.NewGuid(), input = "x" }
                    : op is PhoneHomeOperation.Resize
                        ? new { sessionId = Guid.NewGuid(), cols = 80, rows = 24 }
                        : op is PhoneHomeOperation.KillGeneration
                            ? new { sessionId = Guid.NewGuid(), expectedAcceptedStartedAt = DateTime.UtcNow }
                            : op is PhoneHomeOperation.ConditionalInput
                                ? new RunnerConditionalInputRequest(DateTime.UtcNow, 0, "x") { }
                                : new { sessionId = Guid.NewGuid() };
            if (op == PhoneHomeOperation.ConditionalInput)
            {
                body = new
                {
                    sessionId = Guid.NewGuid(),
                    expectedAcceptedStartedAt = DateTime.UtcNow,
                    expectedLastSequence = 0L,
                    input = "x",
                };
            }

            var frame = new PhoneHomeFrame(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), op,
                JsonSerializer.SerializeToElement(body, PhoneHomeFraming.Json));
            var result = await dispatcher.DispatchAsync(frame, CancellationToken.None);
            result.Kind.ShouldBeOneOf(PhoneHomeFrameKind.Result, PhoneHomeFrameKind.Error);
            if (op is PhoneHomeOperation.Capabilities or PhoneHomeOperation.Health or PhoneHomeOperation.List
                or PhoneHomeOperation.Launch or PhoneHomeOperation.Input)
                result.Kind.ShouldBe(PhoneHomeFrameKind.Result);
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

        await using var host = await PhoneHomeTestHost.StartAsync(limits: new PhoneHomeLimits(MaxMessageUtf8Bytes: 256));
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        var huge = new string('z', 1024);
        var accepted = true;
        try
        {
            await PhoneHomeFraming.WriteFrameAsync(
                peer.Socket,
                new PhoneHomeFrame(PhoneHomeFrameKind.Event, live.Epoch, Guid.Empty, EventName: "SessionTranscript",
                    Payload: JsonSerializer.SerializeToElement(new { text = huge }, PhoneHomeFraming.Json)),
                256,
                CancellationToken.None);
        }
        catch (PhoneHomeTransportException)
        {
            accepted = false;
        }

        oversizedMessageAccepted = accepted;
        oversizedMessageAccepted.ShouldBeFalse();
    }

    [Test]
    public async Task Request_limit_refuses_the_thirty_third_request()
    {
        var limits = new PhoneHomeLimits(MaxInFlightRequests: 32);
        await using var host = await PhoneHomeTestHost.StartAsync(limits: limits);
        await using var peer = await host.ConnectPeerAsync(autoReply: false);
        var live = await host.WaitLiveAsync();
        var client = new PhoneHomeRunnerClient(live);
        var waiters = new List<Task>();
        for (var i = 0; i < 32; i++)
            waiters.Add(client.GetHealthAsync(CancellationToken.None));

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (live.InFlight < 32 && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        var peerOutstandingRequests = live.InFlight;
        peerOutstandingRequests.ShouldBe(limits.MaxInFlightRequests);
        await Should.ThrowAsync<PhoneHomeTransportException>(() => client.GetHealthAsync(CancellationToken.None));
        waiters.Count(t => t.IsCompleted).ShouldBe(0);
    }

    private static AgentLaunchSpec DummySpec() =>
        new("grok", AgentKind.Grok, "grok", [], new Dictionary<string, string>(), "/work", 80, 24);

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
}
