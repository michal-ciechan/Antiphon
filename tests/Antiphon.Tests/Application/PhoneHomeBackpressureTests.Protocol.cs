using System.Net.WebSockets;
using System.Text.Json;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.SessionRunner.Tests.TestHelpers;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Antiphon.Tests.Application;

public partial class PhoneHomeBackpressureTests
{
    private static async Task ProtocolGuardsAsync()
    {
        var id = Guid.NewGuid();
        await using (var p = new ProtocolRunner(new(1, 1, 4096)))
        {
            p.Output(id, 1, "first");
            await PhoneHomeHostedRunner.UntilAsync(() => p.Events.Count == 1, "first window reservation sends");
            var bytes = PhoneHomeEventPayload.Measure(p.Events[0].Payload);
            p.Output(id, 2, "second");
            await PhoneHomeHostedRunner.UntilAsync(() => p.Service.WaitingForEventCredit, "second event waits before the writer gate");
            p.Update(6, 1, bytes); // stale epoch
            await p.BarrierAsync();
            p.Events.Count.ShouldBe(1);
            p.Service.WaitingForEventCredit.ShouldBeTrue();
            p.Update(7, 0, 0); // duplicate initial totals
            await p.BarrierAsync();
            p.Events.Count.ShouldBe(1);
            p.Update(7, 1, bytes);
            await PhoneHomeHostedRunner.UntilAsync(() => p.Events.Count == 2, "valid release wakes the real sender");
            p.Update(7, 1, bytes); // duplicate is harmless
            await p.BarrierAsync();
            p.Events.Count.ShouldBe(2);
            p.Update(7, 3, bytes); // beyond the two reserved sends
            (await Should.ThrowAsync<PhoneHomeTransportException>(() => p.Loop.WaitAsync(LoadTimeoutBounds.PhoneHomeSignalWait)))
                .Code.ShouldBe(PhoneHomeProblemTypes.EventWindowInvalid);
            p.Service.WaitingForEventCredit.ShouldBeFalse();
            p.Service.EventPressure.PendingEvents.ShouldBe(0);
        }
        foreach (var malformed in new[] { new PhoneHomeEventWindowOptions(2, 1, 10), new(1, 0, 10), new(1, 1, 0) })
        {
            await using var p = new ProtocolRunner(malformed);
            (await Should.ThrowAsync<PhoneHomeTransportException>(() => p.Loop)).Code.ShouldBe(PhoneHomeProblemTypes.EventWindowInvalid);
            p.Events.ShouldBeEmpty();
        }
        foreach (var invalid in new[] { "{}", "{\"releasedEvents\":-1,\"releasedPayloadBytes\":0}",
                     "{\"releasedEvents\":0,\"releasedPayloadBytes\":1}" })
        {
            await using var p = new ProtocolRunner(new(1, 1, 4096));
            p.Socket.Enqueue(new(PhoneHomeFrameKind.EventWindow, 7, Guid.Empty,
                Payload: JsonDocument.Parse(invalid).RootElement.Clone()));
            (await Should.ThrowAsync<PhoneHomeTransportException>(() => p.Loop.WaitAsync(LoadTimeoutBounds.PhoneHomeSignalWait)))
                .Code.ShouldBe(PhoneHomeProblemTypes.EventWindowInvalid);
        }
        await using (var p = new ProtocolRunner(new(1, 2, 4096)))
        {
            p.Output(id, 1, "one");
            p.Output(id, 2, "two");
            await PhoneHomeHostedRunner.UntilAsync(() => p.Events.Count == 2, "both available count credits send");
            var bytes = PhoneHomeEventPayload.Measure(p.Events[0].Payload);
            p.Update(7, 1, bytes);
            await p.BarrierAsync();
            p.Update(7, 0, bytes);
            (await Should.ThrowAsync<PhoneHomeTransportException>(() => p.Loop.WaitAsync(LoadTimeoutBounds.PhoneHomeSignalWait)))
                .Code.ShouldBe(PhoneHomeProblemTypes.EventWindowInvalid);
        }
        foreach (var variant in new[] { "bytes-beyond-sent", "bytes-backwards", "same-count-changed-bytes" })
        {
            await using var p = new ProtocolRunner(new(1, 2, 4096));
            p.Output(id, 1, "one"); p.Output(id, 2, "two");
            await PhoneHomeHostedRunner.UntilAsync(() => p.Events.Count == 2, "two sent events establish cumulative bounds");
            var firstBytes = PhoneHomeEventPayload.Measure(p.Events[0].Payload);
            var totalBytes = p.Events.Sum(e => PhoneHomeEventPayload.Measure(e.Payload));
            p.Update(7, 1, firstBytes);
            await p.BarrierAsync();
            p.Update(7, variant == "same-count-changed-bytes" ? 1 : 2,
                variant == "bytes-beyond-sent" ? totalBytes + 1 : variant == "bytes-backwards" ? firstBytes - 1 : firstBytes + 1);
            (await Should.ThrowAsync<PhoneHomeTransportException>(() => p.Loop.WaitAsync(LoadTimeoutBounds.PhoneHomeSignalWait)))
                .Code.ShouldBe(PhoneHomeProblemTypes.EventWindowInvalid, variant);
        }
        await using (var p = new ProtocolRunner(new(1, 8, 100)))
        {
            p.Output(id, 1, new string('x', 101));
            var error = await Should.ThrowAsync<PhoneHomeTransportException>(() => p.Loop.WaitAsync(LoadTimeoutBounds.PhoneHomeSignalWait));
            error.Code.ShouldBe(PhoneHomeProblemTypes.EventWindowTooSmall);
            error.Message.ShouldContain("UTF-8 bytes");
            p.Events.ShouldBeEmpty();
        }
        await using (var p = new ProtocolRunner(null))
        {
            for (var i = 1; i <= 12; i++)
            {
                p.Output(id, i, "legacy");
                var expected = i;
                await PhoneHomeHostedRunner.UntilAsync(() => p.Events.Count == expected, "absent window keeps legacy sending");
            }
            p.Socket.Enqueue(new(PhoneHomeFrameKind.EventWindow, 7, Guid.Empty, Payload: JsonSerializer.SerializeToElement(new { bad = true })));
            await p.BarrierAsync();
            p.Loop.IsCompleted.ShouldBeFalse("an unsolicited frame must never negotiate credits");
        }
        await using (var p = new ProtocolRunner(new(1, 1, 4096)))
        {
            p.Output(id, 1, "one");
            await PhoneHomeHostedRunner.UntilAsync(() => p.Events.Count == 1, "one sent before cancellation");
            p.Output(id, 2, "two");
            await PhoneHomeHostedRunner.UntilAsync(() => p.Service.WaitingForEventCredit, "credit waiter is established");
            await p.StopAsync();
            p.Service.WaitingForEventCredit.ShouldBeFalse();
            p.Service.EventPressure.PendingEvents.ShouldBe(0);
        }
        await CreditUpdateDuringSendAsync();
        await MalformedRegistrationAsync();
    }

    private static async Task MalformedRegistrationAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "c1523-registration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var settings = new PhoneHomeSettings { ServerOrigin = "http://127.0.0.1:1", RunnerId = "test",
                SecretPath = Path.Combine(root, "secret"), StoreIdPath = Path.Combine(root, "store"),
                CapacityStatePath = Path.Combine(root, "capacity"), LaunchGenerationsPath = Path.Combine(root, "generations") };
            await File.WriteAllTextAsync(settings.SecretPath, "test-only");
            var runtime = new SessionRunnerRuntime(Options.Create(new Antiphon.SessionRunner.SessionRunnerSettings
                { SessionLogPath = Path.Combine(root, "sessions") }), NullLogger<SessionRunnerRuntime>.Instance);
            using var service = new PhoneHomeConnectionService(Options.Create(settings), new PhoneHomeAdoptionGate(),
                new PhoneHomeCommandDispatcher(new PhoneHomeRuntimeAdapter(runtime,
                    new RunnerBuildDto("test", null, DateTime.UtcNow, DateTime.UtcNow)), settings), runtime,
                new InvalidRegistrationFactory(), TimeProvider.System, NullLogger<PhoneHomeConnectionService>.Instance);
            (await Should.ThrowAsync<PhoneHomeTransportException>(() => service.RunConnectionAsync(CancellationToken.None)))
                .Code.ShouldBe(PhoneHomeProblemTypes.EventWindowInvalid, "malformed registration fails before a socket connect or zero-credit wait");
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class InvalidRegistrationFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new InvalidRegistrationHandler());
        private sealed class InvalidRegistrationHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
                Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new PhoneHomeRegistrationResponse("test-ticket",
                        DateTimeOffset.UtcNow.AddSeconds(30), Guid.NewGuid(), Guid.NewGuid(), 7, new(0, 1, 100)), PhoneHomeFraming.Json),
                        System.Text.Encoding.UTF8, "application/json")
                });
        }
    }

    private static async Task CreditUpdateDuringSendAsync()
    {
        using var socket = new PhoneHomeTestWebSocket();
        await using var live = new PhoneHomeLiveConnection("test", Guid.NewGuid(), Guid.NewGuid(), 11, socket,
            new PhoneHomeLimits(MaxPendingEvents: 2, MaxPendingEventBytes: 4096), TimeProvider.System,
            eventWindow: new(1, 2, 4096));
        using var stop = new CancellationTokenSource();
        var receive = live.ReceiveLoopAsync(stop.Token);
        try
        {
            for (var i = 0; i < 2; i++) socket.Enqueue(new(PhoneHomeFrameKind.Event, 11, Guid.Empty,
                EventName: "unknown", Payload: JsonSerializer.SerializeToElement(new { value = i })));
            var first = await live.Events.ReadAsync().AsTask().WaitAsync(LoadTimeoutBounds.PhoneHomeSignalWait);
            var second = await live.Events.ReadAsync().AsTask().WaitAsync(LoadTimeoutBounds.PhoneHomeSignalWait);
            var hold = socket.HoldNextSend();
            first.Release();
            await hold.Entered.Task.WaitAsync(LoadTimeoutBounds.PhoneHomeSignalWait);
            second.Release();
            first.Release(); // lease idempotence must not grant a third credit
            hold.Release.TrySetResult();
            await PhoneHomeHostedRunner.UntilAsync(() => socket.Sent.Count(f => f.Kind == PhoneHomeFrameKind.EventWindow) == 2,
                "a release during the send must not lose its notification");
            var updates = socket.Sent.Where(f => f.Kind == PhoneHomeFrameKind.EventWindow)
                .Select(f => f.Payload!.Value.Deserialize<PhoneHomeEventWindowUpdate>(PhoneHomeFraming.Json)!).ToArray();
            updates.Select(u => u.ReleasedEvents).ShouldBe(new long[] { 1, 2 });
            updates[1].ReleasedPayloadBytes.ShouldBe(first.PayloadBytes + second.PayloadBytes);
            live.PendingEvents.ShouldBe(0);
            live.PendingEventBytes.ShouldBe(0);
            socket.Overlaps.ShouldBe(0);
        }
        finally
        {
            stop.Cancel();
            try { await receive; } catch (OperationCanceledException) { }
        }
    }

    private sealed class ProtocolRunner : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "c1523-protocol-" + Guid.NewGuid().ToString("N"));
        private readonly CancellationTokenSource _stop = new();
        public PhoneHomeTestWebSocket Socket { get; } = new();
        public PhoneHomeConnectionService Service { get; }
        public SessionRunnerRuntime Runtime { get; }
        public Task Loop { get; }
        public IReadOnlyList<PhoneHomeFrame> Events => Socket.Sent.Where(f => f.Kind == PhoneHomeFrameKind.Event).ToArray();
        public ProtocolRunner(PhoneHomeEventWindowOptions? window)
        {
            Directory.CreateDirectory(_root);
            Runtime = new SessionRunnerRuntime(Options.Create(new Antiphon.SessionRunner.SessionRunnerSettings
                { SessionLogPath = Path.Combine(_root, "sessions") }), NullLogger<SessionRunnerRuntime>.Instance);
            var settings = new PhoneHomeSettings { LaunchGenerationsPath = Path.Combine(_root, "generations"),
                CapacityStatePath = Path.Combine(_root, "capacity") };
            Service = new PhoneHomeConnectionService(Options.Create(settings), new PhoneHomeAdoptionGate(),
                new PhoneHomeCommandDispatcher(new PhoneHomeRuntimeAdapter(Runtime,
                    new RunnerBuildDto("test", null, DateTime.UtcNow, DateTime.UtcNow)), settings), Runtime,
                new HttpFactory(), TimeProvider.System, NullLogger<PhoneHomeConnectionService>.Instance);
            Loop = Service.RunConnectedAsync(Socket, 7, window, _stop.Token);
        }
        public void Output(Guid session, long sequence, string text) => Runtime.EventHubForTest.Publish(
            SessionRunnerEventNames.SessionOutput, new RunnerOutputEvent(session, sequence, text));
        public void Update(long epoch, long count, long bytes) => Socket.Enqueue(new(PhoneHomeFrameKind.EventWindow, epoch, Guid.Empty,
            Payload: JsonSerializer.SerializeToElement(new PhoneHomeEventWindowUpdate(count, bytes), PhoneHomeFraming.Json)));
        public async Task BarrierAsync()
        {
            var id = Guid.NewGuid();
            Socket.Enqueue(new(PhoneHomeFrameKind.Request, 7, id, PhoneHomeOperation.Health));
            await PhoneHomeHostedRunner.UntilAsync(() => Socket.Sent.Any(f => f.Kind == PhoneHomeFrameKind.Result && f.RequestId == id),
                "RPC reply proves the receive loop processed preceding updates");
        }
        public async Task StopAsync()
        {
            _stop.Cancel();
            try { await Loop.WaitAsync(LoadTimeoutBounds.PhoneHomeSignalWait); } catch (OperationCanceledException) { }
        }
        public async ValueTask DisposeAsync()
        {
            try { await StopAsync(); } catch (PhoneHomeTransportException) { }
            Service.Dispose();
            Socket.Dispose();
            _stop.Dispose();
            Directory.Delete(_root, true);
        }
        private sealed class HttpFactory : IHttpClientFactory { public HttpClient CreateClient(string name) => new(); }
    }
}
