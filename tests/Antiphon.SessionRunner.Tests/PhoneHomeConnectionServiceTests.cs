using Antiphon.SessionRunner.Contracts;
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
        public int OwnedSessionCount => 0;
        public RunnerCapabilitiesDto Capabilities() =>
            new("InboxConhost", "inbox", "test", false, Features: [], VerificationCustodyBackend: null);
        public string Health() => "Healthy";
        public IReadOnlyList<RunnerSessionDto> List() => [];
        public Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => throw new KeyNotFoundException();
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
