using System.Net.WebSockets;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Antiphon.SessionRunner.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
public class PhoneHomeConnectionServiceTests
{
    [Test]
    public async Task Adoption_precedes_registration()
    {
        var gate = new PhoneHomeAdoptionGate();
        var registrationRequests = new List<int>();
        var handler = new RecordingHandler(registrationRequests);
        var settings = Options.Create(new PhoneHomeSettings
        {
            Enabled = true,
            RunnerId = "grok-linux",
            ServerOrigin = "http://127.0.0.1:1",
            SecretPath = WriteTemp("secret"),
            StoreIdPath = Path.Combine(Path.GetTempPath(), "c490-store-" + Guid.NewGuid().ToString("N")),
            AllowedCwd = "/work",
            Capacity = 1,
        });
        var dispatcher = new PhoneHomeCommandDispatcher(new RecordingRuntime(), settings.Value);
        var runtime = new SessionRunnerRuntime(
            Options.Create(new SessionRunnerSettings
            {
                SessionLogPath = Path.Combine(Path.GetTempPath(), "c490-runtime-" + Guid.NewGuid().ToString("N")),
            }),
            NullLogger<SessionRunnerRuntime>.Instance);
        var service = new PhoneHomeConnectionService(
            settings,
            gate,
            dispatcher,
            runtime,
            new SingleHandlerFactory(handler),
            TimeProvider.System,
            NullLogger<PhoneHomeConnectionService>.Instance);
        using var cts = new CancellationTokenSource();
        var running = service.StartAsync(cts.Token);
        await Task.Delay(200);
        registrationRequests.Count.ShouldBe(0);
        gate.SignalReady();
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && registrationRequests.Count == 0)
            await Task.Delay(50);
        registrationRequests.Count.ShouldBe(1);
        cts.Cancel();
        try { await running; } catch (OperationCanceledException) { /* expected */ }
    }

    [Test]
    public async Task Hub_overflow_disconnects_for_recovery()
    {
        var overflowNotified = false;
        var hub = new SessionRunnerEventHub();
        using var cts = new CancellationTokenSource();
        var reader = hub.SubscribeBounded(2, 100, () => overflowNotified = true, cts.Token);
        hub.Publish("a", new { n = 1 });
        hub.Publish("a", new { n = 2 });
        hub.Publish("a", new { n = 3 });
        overflowNotified.ShouldBeTrue();
        overflowNotified = false;
        using var cts2 = new CancellationTokenSource();
        hub.SubscribeBounded(8, 8, () => overflowNotified = true, cts2.Token);
        hub.Publish("b", new { payload = new string('x', 32) });
        overflowNotified.ShouldBeTrue();
        await Task.CompletedTask;
        _ = reader;
    }

    [Test]
    public async Task Draining_event_loop_accepts_more_than_max_pending_without_overflow()
    {
        const int max = 8;
        const int rounds = 40;
        var overflow = false;
        var hub = new SessionRunnerEventHub();
        using var cts = new CancellationTokenSource();
        var reader = hub.SubscribeBounded(max, 1_000_000, () => overflow = true, cts.Token);
        var lease = (ISessionRunnerEventLease)reader;
        var socket = new PhoneHomeTestWebSocket();
        var writer = new PhoneHomeConnectionWriter(socket, PhoneHomeProtocol.DefaultMaxMessageUtf8Bytes);
        var loop = Service(new RecordingRuntime()).EventLoopAsync(writer, 1, reader, cts.Token);

        for (var round = 0; round < rounds; round++)
        {
            var drained = await Until(() => lease.PendingEvents == 0 && !overflow);
            drained.ShouldBeTrue($"pending depth stuck at {lease.PendingEvents} after {socket.Sent.Count} sent events");
            for (var i = 0; i < max; i++)
                hub.Publish("session", new { n = round * max + i });
            var target = (round + 1) * max;
            var sent = await Until(() => socket.Sent.Count >= target || overflow);
            sent.ShouldBeTrue($"sent {socket.Sent.Count} of {target}");
            overflow.ShouldBeFalse();
        }

        var idle = await Until(() => lease.PendingEvents == 0 && lease.PendingBytes == 0);
        idle.ShouldBeTrue($"pending events {lease.PendingEvents} bytes {lease.PendingBytes}");
        socket.Sent.Count.ShouldBe(max * rounds);
        cts.Cancel();
        await EndsQuiet(loop);
    }

    [Test]
    public async Task Stalled_event_send_still_overflows()
    {
        var overflow = false;
        var hub = new SessionRunnerEventHub();
        using var cts = new CancellationTokenSource();
        var reader = hub.SubscribeBounded(2, 1_000_000, () => overflow = true, cts.Token);
        var lease = (ISessionRunnerEventLease)reader;
        var socket = new PhoneHomeTestWebSocket();
        var writer = new PhoneHomeConnectionWriter(socket, PhoneHomeProtocol.DefaultMaxMessageUtf8Bytes);
        var hold = socket.HoldNextSend();
        var loop = Service(new RecordingRuntime()).EventLoopAsync(writer, 1, reader, cts.Token);

        hub.Publish("session", new { n = 1 });
        await hold.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        socket.Sent.Count.ShouldBe(0);
        lease.PendingEvents.ShouldBe(1);

        hub.Publish("session", new { n = 2 });
        lease.PendingEvents.ShouldBe(2);
        hub.Publish("session", new { n = 3 });
        overflow.ShouldBeTrue();
        lease.PendingEvents.ShouldBe(2);

        hold.Release.TrySetResult();
        cts.Cancel();
        await EndsQuiet(loop);
    }

    [Test]
    public async Task Held_command_does_not_block_receive_progress()
    {
        var held = new TaskCompletionSource<RunnerSessionDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new RecordingRuntime { StartHold = held };
        var dispatcher = new PhoneHomeCommandDispatcher(runtime, new PhoneHomeSettings
        {
            Enabled = true,
            AllowedCwd = "/work",
            Capacity = 1,
        });
        var launch = dispatcher.DispatchAsync(
            new PhoneHomeFrame(
                PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.Launch,
                System.Text.Json.JsonSerializer.SerializeToElement(
                    new RunnerLaunchRequest(Guid.NewGuid(), "grok", [], new Dictionary<string, string>(), "/work", 80, 24),
                    PhoneHomeFraming.Json)),
            CancellationToken.None);
        var receiveProgressBeforeLaunchRelease = false;
        var health = await dispatcher.DispatchAsync(
            new PhoneHomeFrame(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.Health),
            CancellationToken.None);
        health.Kind.ShouldBe(PhoneHomeFrameKind.Result);
        receiveProgressBeforeLaunchRelease = !launch.IsCompleted;
        receiveProgressBeforeLaunchRelease.ShouldBeTrue();
        held.SetResult(new RunnerSessionDto(Guid.NewGuid(), 1, DateTime.UtcNow, "Running", null, "", 0));
        var launched = await launch.WaitAsync(TimeSpan.FromSeconds(3));
        launched.Kind.ShouldBe(PhoneHomeFrameKind.Result);
    }

    // --- CARD-0631 D-2/D-3: the receive pump answers every request it accepted. ---

    [Test]
    public async Task Receive_loop_writes_error_frame_when_dispatch_throws()
    {
        const long epoch = 11;
        var runtime = new RecordingRuntime
        {
            // An OCE nobody asked for: it escapes the dispatcher's catch by design, so only the
            // receive pump's own backstop can answer it.
            GetFault = () => new OperationCanceledException("unrelated cancellation"),
        };
        var service = Service(runtime);
        var socket = new PhoneHomeTestWebSocket();
        var writer = new PhoneHomeConnectionWriter(socket, PhoneHomeProtocol.DefaultMaxMessageUtf8Bytes);
        using var cts = new CancellationTokenSource();
        var pump = service.ReceiveLoopAsync(writer, epoch, cts.Token);

        var get = Request(PhoneHomeOperation.Get, epoch, new { sessionId = Guid.NewGuid() });
        socket.Enqueue(get);
        (await Eventually(() => socket.Sent.Count >= 1)).ShouldBeTrue("the escaped dispatch must still produce a reply frame");
        (await Eventually(() => service.InFlight == 0)).ShouldBeTrue();
        socket.Sent.Count.ShouldBe(1);
        var error = socket.Sent[0];
        error.Kind.ShouldBe(PhoneHomeFrameKind.Error);
        error.ErrorCode.ShouldBe(PhoneHomeProblemTypes.RunnerInternalError);
        error.StatusCode.ShouldBe(500);
        error.Epoch.ShouldBe(epoch);
        error.RequestId.ShouldBe(get.RequestId);
        error.Operation.ShouldBe(PhoneHomeOperation.Get);
        error.ErrorDetail!.ShouldStartWith(nameof(OperationCanceledException));

        // The connection is still serving: the next request is answered normally.
        var health = Request(PhoneHomeOperation.Health, epoch);
        socket.Enqueue(health);
        (await Eventually(() => socket.Sent.Count >= 2)).ShouldBeTrue();
        socket.Sent[1].Kind.ShouldBe(PhoneHomeFrameKind.Result);
        socket.Sent[1].RequestId.ShouldBe(health.RequestId);
        (await Eventually(() => service.InFlight == 0)).ShouldBeTrue();

        socket.CompleteIncoming();
        await pump.WaitAsync(TimeSpan.FromSeconds(5));

        // Shutdown: the connection is cancelled while the handler waits. That is not a fault to
        // report - no synthetic Error - and the in-flight count still returns to zero.
        var held = new RecordingRuntime { GetHold = new TaskCompletionSource<RunnerSessionDto>(TaskCreationOptions.RunContinuationsAsynchronously) };
        var shutdownService = Service(held);
        var shutdownSocket = new PhoneHomeTestWebSocket();
        var shutdownWriter = new PhoneHomeConnectionWriter(shutdownSocket, PhoneHomeProtocol.DefaultMaxMessageUtf8Bytes);
        using var shutdownCts = new CancellationTokenSource();
        var shutdownPump = shutdownService.ReceiveLoopAsync(shutdownWriter, epoch, shutdownCts.Token);
        shutdownSocket.Enqueue(Request(PhoneHomeOperation.Get, epoch, new { sessionId = Guid.NewGuid() }));
        await held.GetEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        shutdownService.InFlight.ShouldBe(1);
        shutdownCts.Cancel();
        (await EndsCancelled(shutdownPump)).ShouldBeTrue("the pump ends by cancellation");
        (await Eventually(() => shutdownService.InFlight == 0)).ShouldBeTrue("the in-flight decrement must run after cancellation");
        held.GetFinished.ShouldBeTrue();
        shutdownSocket.State.ShouldBe(WebSocketState.Open);
        shutdownSocket.Sent.ShouldBeEmpty();
    }

    // --- CARD-0631 D-4: one connection, one writer. ---

    [Test]
    public async Task Concurrent_reply_and_heartbeat_never_drop_a_reply()
    {
        const long epoch = 5;
        var faulting = new RecordingRuntime { GetFault = () => new OperationCanceledException("unrelated cancellation") };
        var producers = new (string Name, Func<PhoneHomeConnectionService, PhoneHomeConnectionWriter, Guid, Task> Send, PhoneHomeFrameKind Kind, PhoneHomeConnectionService Service)[]
        {
            ("reply", (svc, w, id) => svc.DispatchAndReplyAsync(w, Request(PhoneHomeOperation.Health, epoch) with { RequestId = id }, epoch, CancellationToken.None),
                PhoneHomeFrameKind.Result, Service(new RecordingRuntime())),
            ("event", (svc, w, _) => svc.SendEventAsync(w, epoch, new RunnerServerSentEvent("session", "{\"n\":1}"), CancellationToken.None),
                PhoneHomeFrameKind.Event, Service(new RecordingRuntime())),
            ("request-limit", (svc, w, id) => svc.SendRequestLimitAsync(w, epoch, Request(PhoneHomeOperation.Input, epoch) with { RequestId = id }, CancellationToken.None),
                PhoneHomeFrameKind.Error, Service(new RecordingRuntime())),
            ("fallback-error", (svc, w, id) => svc.DispatchAndReplyAsync(w, Request(PhoneHomeOperation.Get, epoch, new { sessionId = Guid.NewGuid() }) with { RequestId = id }, epoch, CancellationToken.None),
                PhoneHomeFrameKind.Error, Service(faulting)),
        };

        foreach (var (name, send, kind, service) in producers)
        {
            var socket = new PhoneHomeTestWebSocket();
            var writer = new PhoneHomeConnectionWriter(socket, PhoneHomeProtocol.DefaultMaxMessageUtf8Bytes);
            var hold = socket.HoldNextSend();
            var heartbeat = service.SendHeartbeatAsync(writer, epoch, CancellationToken.None);
            await hold.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var id = Guid.NewGuid();
            var producer = send(service, writer, id);
            // The producer's frame is ready synchronously; it must now be queued behind the
            // held heartbeat, not writing the socket beside it.
            producer.IsCompleted.ShouldBeFalse(name + " must wait for the held heartbeat");
            socket.Overlaps.ShouldBe(0, name);

            hold.Release.SetResult();
            await heartbeat.WaitAsync(TimeSpan.FromSeconds(5));
            await producer.WaitAsync(TimeSpan.FromSeconds(5));

            socket.Overlaps.ShouldBe(0, name);
            socket.PeakConcurrentSends.ShouldBe(1, name);
            socket.State.ShouldBe(WebSocketState.Open, name);
            var sent = socket.Sent;
            sent.Count.ShouldBe(2, name + " must send exactly the heartbeat and its own frame");
            sent[0].Kind.ShouldBe(PhoneHomeFrameKind.Heartbeat, name);
            sent[1].Kind.ShouldBe(kind, name);
            if (kind != PhoneHomeFrameKind.Event)
                sent[1].RequestId.ShouldBe(id, name + " reply must carry its own request id");
            sent[1].Epoch.ShouldBe(epoch, name);
        }

        // A failed send releases the gate: the next standalone send completes.
        var failing = new PhoneHomeTestWebSocket();
        var failingWriter = new PhoneHomeConnectionWriter(failing, PhoneHomeProtocol.DefaultMaxMessageUtf8Bytes);
        failing.FailNextSend(new WebSocketException(WebSocketError.ConnectionClosedPrematurely));
        await Should.ThrowAsync<WebSocketException>(() => failingWriter.SendAsync(Heartbeat(epoch), CancellationToken.None));
        var afterFailure = failingWriter.SendAsync(Heartbeat(epoch), CancellationToken.None);
        (await Task.WhenAny(afterFailure, Task.Delay(TimeSpan.FromSeconds(5)))).ShouldBe(afterFailure, "the gate must be released after a failed send");
        await afterFailure;
        failing.Sent.Count.ShouldBe(1);

        // A queued send is cancelled by its own token; the gate still serves the next sender.
        var queued = new PhoneHomeTestWebSocket();
        var queuedWriter = new PhoneHomeConnectionWriter(queued, PhoneHomeProtocol.DefaultMaxMessageUtf8Bytes);
        var queuedHold = queued.HoldNextSend();
        var first = queuedWriter.SendAsync(Heartbeat(epoch), CancellationToken.None);
        await queuedHold.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var queuedCts = new CancellationTokenSource();
        var cancelled = queuedWriter.SendAsync(Heartbeat(epoch), queuedCts.Token);
        queuedCts.Cancel();
        (await EndsCancelled(cancelled)).ShouldBeTrue("a queued send observes its own cancellation");
        queuedHold.Release.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        var third = queuedWriter.SendAsync(Heartbeat(epoch), CancellationToken.None);
        (await Task.WhenAny(third, Task.Delay(TimeSpan.FromSeconds(5)))).ShouldBe(third);
        queued.Sent.Count.ShouldBe(2);
        queued.Overlaps.ShouldBe(0);

        // A reply queued on a connection that then ends never reaches the replacement socket.
        var old = new PhoneHomeTestWebSocket();
        var oldWriter = new PhoneHomeConnectionWriter(old, PhoneHomeProtocol.DefaultMaxMessageUtf8Bytes);
        var oldHold = old.HoldNextSend();
        var service2 = Service(new RecordingRuntime());
        var oldHeartbeat = service2.SendHeartbeatAsync(oldWriter, epoch, CancellationToken.None);
        await oldHold.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var oldConnection = new CancellationTokenSource();
        var staleReply = service2.DispatchAndReplyAsync(oldWriter, Request(PhoneHomeOperation.Health, epoch), epoch, oldConnection.Token);
        oldConnection.Cancel();
        await staleReply.WaitAsync(TimeSpan.FromSeconds(5));
        var replacement = new PhoneHomeTestWebSocket();
        _ = new PhoneHomeConnectionWriter(replacement, PhoneHomeProtocol.DefaultMaxMessageUtf8Bytes);
        oldHold.Release.SetResult();
        await oldHeartbeat.WaitAsync(TimeSpan.FromSeconds(5));
        old.Sent.Select(f => f.Kind).ShouldBe([PhoneHomeFrameKind.Heartbeat]);
        replacement.Sent.ShouldBeEmpty();
    }

    private static PhoneHomeFrame Request(PhoneHomeOperation operation, long epoch, object? payload = null) =>
        new(PhoneHomeFrameKind.Request, epoch, Guid.NewGuid(), operation,
            payload is null ? null : JsonSerializer.SerializeToElement(payload, PhoneHomeFraming.Json));

    private static PhoneHomeFrame Heartbeat(long epoch) => new(PhoneHomeFrameKind.Heartbeat, epoch, Guid.NewGuid());

    private static async Task<bool> EndsCancelled(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(5));
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }

    private static async Task<bool> Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(5);
        }

        return condition();
    }

    private static async Task EndsQuiet(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task<bool> Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(20);
        }

        return condition();
    }

    private static PhoneHomeConnectionService Service(IPhoneHomeRuntimeSurface surface)
    {
        var settings = Options.Create(new PhoneHomeSettings
        {
            Enabled = true,
            RunnerId = "grok-linux",
            ServerOrigin = "http://127.0.0.1:1",
            AllowedCwd = "/work",
            Capacity = 1,
        });
        var runtime = new SessionRunnerRuntime(
            Options.Create(new SessionRunnerSettings
            {
                SessionLogPath = Path.Combine(Path.GetTempPath(), "c631-runtime-" + Guid.NewGuid().ToString("N")),
            }),
            NullLogger<SessionRunnerRuntime>.Instance);
        return new PhoneHomeConnectionService(
            settings,
            new PhoneHomeAdoptionGate(),
            new PhoneHomeCommandDispatcher(surface, settings.Value),
            runtime,
            new SingleHandlerFactory(new RecordingHandler([])),
            TimeProvider.System,
            NullLogger<PhoneHomeConnectionService>.Instance);
    }

    private static string WriteTemp(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "c490-" + Guid.NewGuid().ToString("N") + name);
        File.WriteAllText(path, "test-secret");
        return path;
    }

    private sealed class RecordingHandler(List<int> registrations) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.Contains("register", StringComparison.OrdinalIgnoreCase) == true)
                registrations.Add(1);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("fail"),
            });
        }
    }

    private sealed class SingleHandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingRuntime : IPhoneHomeRuntimeSurface
    {
        public TaskCompletionSource<RunnerSessionDto>? StartHold { get; set; }

        /// <summary>CARD-0631: what Get throws, and a hold Get waits on (observing its token).</summary>
        public Func<Exception>? GetFault { get; set; }
        public TaskCompletionSource<RunnerSessionDto>? GetHold { get; set; }
        public TaskCompletionSource GetEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public volatile bool GetFinished;
        public int OwnedSessionCount => 0;
        public RunnerCapabilitiesDto Capabilities() =>
            new("InboxConhost", "inbox", "test", false, Features: [], VerificationCustodyBackend: null);
        public string Health() => "Healthy";
        public IReadOnlyList<RunnerSessionDto> List() => [];
        public async Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct)
        {
            GetEntered.TrySetResult();
            try
            {
                if (GetFault is { } fault)
                    throw fault();
                if (GetHold is { } hold)
                    return await hold.Task.WaitAsync(ct);
                throw new KeyNotFoundException();
            }
            finally
            {
                GetFinished = true;
            }
        }
        public async Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct)
        {
            if (StartHold is { } hold)
                return await hold.Task.WaitAsync(ct);
            return new RunnerSessionDto(request.SessionId, 1, DateTime.UtcNow, "Running", null, "", 0);
        }
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
