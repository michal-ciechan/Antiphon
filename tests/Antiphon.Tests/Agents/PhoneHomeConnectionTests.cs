using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
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

    // CARD-0679 D-1: an overflow used to end the connection with no line naming why, and the
    // status route then reported the constant "unavailable".
    [Test]
    public async Task Overflow_disconnect_logs_reason_epoch_and_pending_counts()
    {
        await using var host = await PhoneHomeTestHost.StartAsync(limits: new PhoneHomeLimits(MaxPendingEvents: 2));
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();

        for (var i = 0; i < 4; i++)
        {
            try
            {
                await peer.EmitAsync(OutputEvent(live.Epoch, i));
            }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException)
            {
                break; // the server closed after the overflow
            }
        }

        var ended = await WaitForLogsAsync(host, e => e.Level == LogLevel.Warning && e["Reason"] is not null);
        ended.Count.ShouldBe(1);
        var line = ended[0];
        line["Reason"].ShouldBe(PhoneHomeProblemTypes.EventOverflow);
        line["RunnerId"].ShouldBe(host.AllowedRunnerId);
        line["Epoch"].ShouldBe(live.Epoch);
        line["PendingEvents"].ShouldBe(2);
        host.Directory.Status(host.AllowedRunnerId).DisconnectReason.ShouldBe(PhoneHomeProblemTypes.EventOverflow);
    }

    // CARD-0679 D-1: a runner that drops its socket is a transport event with a reason, not an
    // unhandled-exception Error from the middleware (95 of those on 2026-09-23 named nothing).
    [Test]
    public async Task Peer_abort_is_a_warning_with_transport_abort_not_a_middleware_error()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();

        peer.Socket.Abort();

        var ended = await WaitForLogsAsync(host, e => e.Level == LogLevel.Warning && e["Reason"] is not null);
        ended.Count.ShouldBe(1);
        ended[0]["Reason"].ShouldBe("transport_abort");
        ended[0]["Epoch"].ShouldBe(live.Epoch);
        ended[0]["LifetimeSeconds"].ShouldNotBeNull();
        await Task.Delay(200);
        host.Logs.Entries
            .Where(e => e.Level >= LogLevel.Error && e.Category.EndsWith("ExceptionMiddleware", StringComparison.Ordinal))
            .ShouldBeEmpty();
    }

    // CARD-0679 D-1: the pending-event backlog is visible before the overflow closes the socket.
    [Test]
    public async Task Pending_event_high_water_is_warned_before_overflow()
    {
        await using var host = await PhoneHomeTestHost.StartAsync(limits: new PhoneHomeLimits(MaxPendingEvents: 10));
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        static bool HighWater(CapturedLog e, int percent) =>
            e.Level == LogLevel.Warning && e["Percent"] is int p && p == percent;

        for (var i = 0; i < 6; i++)
            await peer.EmitAsync(OutputEvent(live.Epoch, i));
        var half = await WaitForLogsAsync(host, e => HighWater(e, 50));
        half.Count.ShouldBe(1);
        half[0].Message.ShouldContain("50%");
        half[0]["PendingEvents"].ShouldBe(5);
        half[0]["MaxPendingEvents"].ShouldBe(10);
        live.SocketOpen.ShouldBeTrue();
        host.Logs.Entries.Where(e => HighWater(e, 90)).ShouldBeEmpty();

        for (var i = 6; i < 9; i++)
            await peer.EmitAsync(OutputEvent(live.Epoch, i));
        var high = await WaitForLogsAsync(host, e => HighWater(e, 90));
        high.Count.ShouldBe(1);
        high[0].Message.ShouldContain("90%");
        high[0]["PendingEvents"].ShouldBe(9);
        live.SocketOpen.ShouldBeTrue();

        await peer.EmitAsync(OutputEvent(live.Epoch, 9));
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (live.PendingEvents < 10 && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        live.PendingEvents.ShouldBe(10);
        live.SocketOpen.ShouldBeTrue("the tenth event fits the cap");
        host.Logs.Entries.Count(e => HighWater(e, 50)).ShouldBe(1, "each threshold is warned once per connection");

        await peer.EmitAsync(OutputEvent(live.Epoch, 10));
        var ended = await WaitForLogsAsync(host, e => e.Level == LogLevel.Warning && e["Reason"] is not null);
        ended.Single()["Reason"].ShouldBe(PhoneHomeProblemTypes.EventOverflow);
    }

    // CARD-0679 D-5: a dropped socket used to cancel every waiter, so an acknowledged launch read
    // "A task was canceled." and every catch (OperationCanceledException) took it for the caller.
    [Test]
    public async Task Disconnect_fails_in_flight_requests_with_a_typed_connection_closed_error()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync(autoReply: false);
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        var client = new PhoneHomeRunnerClient(live);

        var pending = client.GetHealthAsync(CancellationToken.None);
        await peer.WaitForAsync(PhoneHomeOperation.Health);
        peer.Socket.Abort();

        Exception? thrown = null;
        try
        {
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        var typed = thrown.ShouldBeOfType<PhoneHomeTransportException>();
        // Review 914a96fd D1: the request was sent, so the runner may have acted on it. That is
        // never the never-sent code, which the queue treats as unreachable and refunds.
        typed.Code.ShouldBe("phone_home_connection_closed_in_flight");
        typed.Message.ShouldContain(host.AllowedRunnerId);
        typed.Message.ShouldContain($"epoch {live.Epoch}");
        typed.Message.ShouldContain(nameof(PhoneHomeOperation.Health));
    }

    // CARD-0679 D-5: the e34a1fcd shape. A send on a socket that already closed surfaced as a raw
    // WebSocketException, which no transport-loss predicate recognised.
    [Test]
    public async Task Send_on_a_closed_connection_is_the_same_typed_error()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);

        peer.Socket.Abort();
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (live.SocketOpen && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        live.SocketOpen.ShouldBeFalse();

        Exception? thrown = null;
        try
        {
            await live.RequestAsync(PhoneHomeOperation.List, null, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        var typed = thrown.ShouldBeOfType<PhoneHomeTransportException>();
        // Review 914a96fd D1: nothing was written, so this is the never-sent code.
        typed.Code.ShouldBe("phone_home_connection_closed_before_send");
        typed.Message.ShouldContain(nameof(PhoneHomeOperation.List));
    }

    // CARD-0716 D-1: a host stop must close the socket with 1001 server_stopping before Kestrel's
    // shutdown timeout aborts it. The 3s host bound is only so a red run does not sit for 30s.
    [Test]
    public async Task Host_stop_sends_a_going_away_close_before_the_socket_dies()
    {
        await using var host = await PhoneHomeTestHost.StartAsync(shutdownTimeout: TimeSpan.FromSeconds(3));
        await using var peer = await host.ConnectPeerAsync();
        await host.WaitLiveAsync();

        _ = Task.Run(() => host.App.Lifetime.StopApplication());

        var completed = await Task.WhenAny(peer.CloseObserved.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        completed.ShouldBe(peer.CloseObserved.Task);
        var (status, description) = await peer.CloseObserved.Task;
        status.ShouldBe(WebSocketCloseStatus.EndpointUnavailable);
        description.ShouldBe("server_stopping");

        var ended = host.Logs.Entries
            .Where(e => e.Level == LogLevel.Warning && e.Message.Contains("ended: request_aborted", StringComparison.Ordinal))
            .ToList();
        ended.Count.ShouldBe(1);
        host.Directory.Status(host.AllowedRunnerId).DisconnectReason.ShouldBe("request_aborted");
    }

    // CARD-0716 D-6: the accept line and the ended line share Kestrel's connection id, and a
    // transport abort names the websocket and socket errors.
    [Test]
    public async Task Accept_and_end_lines_carry_the_connection_id_and_transport_codes()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        await host.WaitLiveAsync();

        peer.Socket.Abort();

        var ended = await WaitForLogsAsync(host, e => e.Level == LogLevel.Warning && e["Reason"] is not null);
        ended.Count.ShouldBe(1);
        var accept = host.Logs.Entries
            .Where(e => e.Level == LogLevel.Information && e.Message.Contains("accepted:", StringComparison.Ordinal))
            .ToList();
        accept.Count.ShouldBe(1);
        var connectionId = accept[0]["ConnectionId"]?.ToString();
        connectionId.ShouldNotBeNullOrWhiteSpace();
        ended[0]["ConnectionId"]?.ToString().ShouldBe(connectionId);
        ended[0]["WsError"]?.ToString().ShouldBe(nameof(WebSocketError.ConnectionClosedPrematurely));
        var socketError = ended[0]["SocketError"]?.ToString();
        socketError.ShouldNotBeNullOrWhiteSpace();
        var namedSocketError = socketError == "none"
            || (Enum.TryParse<SocketError>(socketError, out var parsed) && Enum.IsDefined(parsed));
        namedSocketError.ShouldBeTrue();
    }

    // CARD-0716 repair: overflow used to send the close and then wait out the 3s handshake
    // bound, because the receive loop had already left and nobody read the peer's ack.
    [Test]
    public async Task Overflow_close_finishes_the_handshake()
    {
        await using var host = await PhoneHomeTestHost.StartAsync(limits: new PhoneHomeLimits(MaxPendingEvents: 2));
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();

        for (var i = 0; i < 4; i++)
        {
            try
            {
                await peer.EmitAsync(OutputEvent(live.Epoch, i));
            }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException)
            {
                break;
            }
        }

        await AssertHandshakeClosedAsync(host, live, PhoneHomeProblemTypes.EventOverflow);
    }

    // CARD-0716 repair: a receive fault is the same shape. The loop has thrown, so CloseAsync
    // can complete the handshake instead of aborting after the full bound.
    [Test]
    public async Task Receive_fault_close_finishes_the_handshake()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();

        var bytes = Encoding.UTF8.GetBytes("{not-json");
        await peer.Socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

        await AssertHandshakeClosedAsync(host, live, "receive_fault:JsonException");
    }

    private static async Task AssertHandshakeClosedAsync(
        PhoneHomeTestHost host, PhoneHomeLiveConnection live, string reason)
    {
        var ended = await WaitForLogsAsync(host, e => e.Level == LogLevel.Warning && Equals(e["Reason"], reason));
        ended.Count.ShouldBe(1);
        var deadline = DateTime.UtcNow.AddSeconds(1.5);
        while (live.SocketState is WebSocketState.Open or WebSocketState.CloseSent && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        live.SocketState.ShouldBe(
            WebSocketState.Closed,
            "the close handshake must finish without waiting out the 3s abort bound");
    }

    private static PhoneHomeFrame OutputEvent(long epoch, int n) =>
        new(PhoneHomeFrameKind.Event, epoch, Guid.Empty, EventName: SessionRunnerEventNames.SessionOutput,
            Payload: JsonSerializer.SerializeToElement(new { sessionId = Guid.Empty, text = $"chunk-{n}" }, PhoneHomeFraming.Json));

    // CARD-0727 V-1. server2-temp is configured and offline: 200, not eligible, and none of
    // server2's identity. An id that is not in the map is 404 (CARD-0729).
    [Test]
    public async Task Unknown_runner_status_is_404_and_carries_no_live_runner_identity()
    {
        var store = Guid.NewGuid();
        var secret = "status-secret-" + Guid.NewGuid().ToString("N");
        var configured = RollingRunnerSettings.Pair(secret, secret + "-temp");
        await using var host = await PhoneHomeTestHost.StartAsync(configured: configured);
        await using var peer = await host.ConnectPeerAsync(
            runnerId: RollingRunnerSettings.Server2, storeId: store, secret: secret);
        var live = await host.WaitLiveAsync(runnerId: RollingRunnerSettings.Server2);
        host.Directory.MarkRecovered(live);

        var temp = await host.Http.GetAsync("/api/session-runners/server2-temp/status");
        temp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tempBody = await temp.Content.ReadFromJsonAsync<JsonElement>();
        tempBody.GetProperty("available").GetBoolean().ShouldBeFalse();
        tempBody.GetProperty("dispatchEligible").GetBoolean().ShouldBeFalse();
        IdentityIsNull(tempBody);

        var unknown = await host.Http.GetAsync("/api/session-runners/server2-other/status");
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var unknownText = await unknown.Content.ReadAsStringAsync();
        unknownText.ShouldNotContain("runnerStoreId");
        unknownText.ShouldNotContain("processBootId");
        unknownText.ShouldNotContain("buildVersion");

        var desktop = await host.Http.GetAsync("/api/session-runners/desktop/status");
        desktop.StatusCode.ShouldBe(HttpStatusCode.OK);
        var desktopBody = await desktop.Content.ReadFromJsonAsync<JsonElement>();
        desktopBody.GetProperty("available").GetBoolean().ShouldBeFalse();
        desktopBody.GetProperty("dispatchEligible").GetBoolean().ShouldBeFalse();

        var server2 = await host.Http.GetAsync("/api/session-runners/server2/status");
        server2.StatusCode.ShouldBe(HttpStatusCode.OK);
        var server2Body = await server2.Content.ReadFromJsonAsync<JsonElement>();
        server2Body.GetProperty("dispatchEligible").GetBoolean().ShouldBeTrue();
        server2Body.GetProperty("runnerStoreId").GetGuid().ShouldBe(store);
        peer.ShouldNotBeNull();
    }

    // CARD-0727 V-34. Equal secrets do not make a ticket portable across runner ids.
    [Test]
    public async Task Equal_secrets_do_not_let_a_server2_temp_ticket_connect_as_server2()
    {
        var secret = "equal-secret-" + Guid.NewGuid().ToString("N");
        var configured = RollingRunnerSettings.Pair(secret, secret);
        await using var host = await PhoneHomeTestHost.StartAsync(configured: configured);
        var ticket = await host.RegisterAsync(runnerId: RollingRunnerSettings.Server2Temp, secret: secret);

        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader(PhoneHomeProtocol.TicketHeader, ticket.Ticket);
        var server2Connect = new Uri(
            $"ws://{host.Http.BaseAddress!.Authority}/api/session-runners/server2/connect");
        var refused = false;
        try
        {
            await socket.ConnectAsync(server2Connect, CancellationToken.None);
        }
        catch (WebSocketException)
        {
            refused = true;
        }

        refused.ShouldBeTrue();
        host.Directory.SnapshotLive(RollingRunnerSettings.Server2).ShouldBeNull();
        host.Directory.SnapshotLive(RollingRunnerSettings.Server2Temp).ShouldBeNull();

        await using var peer = await host.ConnectPeerAsync(
            runnerId: RollingRunnerSettings.Server2, storeId: Guid.NewGuid(), secret: secret);
        (await host.WaitLiveAsync(runnerId: RollingRunnerSettings.Server2)).ShouldNotBeNull();
        host.Directory.SnapshotLive(RollingRunnerSettings.Server2Temp).ShouldBeNull();
        peer.Epoch.ShouldBeGreaterThan(0);
    }

    private static void IdentityIsNull(JsonElement body)
    {
        body.GetProperty("runnerStoreId").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("processBootId").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("buildVersion").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    private static async Task<IReadOnlyList<CapturedLog>> WaitForLogsAsync(
        PhoneHomeTestHost host, Func<CapturedLog, bool> match, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
        while (DateTime.UtcNow < deadline)
        {
            var found = host.Logs.Entries.Where(match).ToList();
            if (found.Count > 0)
            {
                await Task.Delay(50); // let a duplicate line, if any, land before the count is read
                return host.Logs.Entries.Where(match).ToList();
            }

            await Task.Delay(20);
        }

        return [];
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
