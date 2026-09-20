using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
public class PhoneHomeCommandDispatcherTests
{
    [Test]
    public async Task Unsupported_operation_or_launch_never_enters_runtime()
    {
        var runtime = new RecordingRuntime();
        var dispatcher = new PhoneHomeCommandDispatcher(runtime, new PhoneHomeSettings
        {
            Enabled = true,
            AllowedCwd = "/work",
            Capacity = 1,
        });

        var herdr = await dispatcher.DispatchAsync(Launch(new RunnerLaunchRequest(
            Guid.NewGuid(), "grok", [], new Dictionary<string, string>(), "/work", 80, 24, Backend: SessionBackends.Herdr)), CancellationToken.None);
        herdr.Kind.ShouldBe(PhoneHomeFrameKind.Error);
        runtime.Mutations.ShouldBeEmpty();
        var runtimeMutations = runtime.Mutations;

        var unknown = await dispatcher.DispatchAsync(new PhoneHomeFrame(
            PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), (PhoneHomeOperation)999), CancellationToken.None);
        unknown.Kind.ShouldBe(PhoneHomeFrameKind.Error);

        var custody = await dispatcher.DispatchAsync(Launch(new RunnerLaunchRequest(
            Guid.NewGuid(), "grok", [], new Dictionary<string, string>(), "/work", 80, 24,
            VerificationBinding: new VerificationExecutionBinding(
                Guid.NewGuid(),
                new VerificationSourceIdentity(Guid.NewGuid(), Guid.NewGuid(), "deadbeef"),
                new VerificationSessionGeneration(Guid.NewGuid(), DateTime.UtcNow),
                new VerificationCreationCoordinates("r", "g", "w", "wg", "main", Guid.NewGuid())))), CancellationToken.None);
        custody.Kind.ShouldBe(PhoneHomeFrameKind.Error);

        var wrongCwd = await dispatcher.DispatchAsync(Launch(new RunnerLaunchRequest(
            Guid.NewGuid(), "grok", [], new Dictionary<string, string>(), "C:\\Windows", 80, 24)), CancellationToken.None);
        wrongCwd.Kind.ShouldBe(PhoneHomeFrameKind.Error);

        runtimeMutations.ShouldBeEmpty();
        runtime.Capabilities().VerificationCustodyBackend.ShouldBeNull();
    }

    [Test]
    public async Task Capacity_counts_adopted_and_concurrent_launches()
    {
        var runtime = new RecordingRuntime { Owned = 1 };
        var dispatcher = new PhoneHomeCommandDispatcher(runtime, new PhoneHomeSettings
        {
            Enabled = true,
            AllowedCwd = "/work",
            Capacity = 1,
        });
        var result = await dispatcher.DispatchAsync(Launch(new RunnerLaunchRequest(
            Guid.NewGuid(), "grok", [], new Dictionary<string, string>(), "/work", 80, 24)), CancellationToken.None);
        result.Kind.ShouldBe(PhoneHomeFrameKind.Error);
        result.ErrorCode.ShouldBe(PhoneHomeProblemTypes.Capacity);
        var maxOwnedSessions = runtime.OwnedSessionCount;
        maxOwnedSessions.ShouldBe(1);
    }

    private static PhoneHomeFrame Launch(RunnerLaunchRequest request) =>
        new(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.Launch,
            System.Text.Json.JsonSerializer.SerializeToElement(request, PhoneHomeFraming.Json));

    private sealed class RecordingRuntime : IPhoneHomeRuntimeSurface
    {
        public int Owned { get; set; }
        public List<string> Mutations { get; } = [];
        public int OwnedSessionCount => Owned;
        public RunnerCapabilitiesDto Capabilities() =>
            new("InboxConhost", "inbox", "test", false, Features: [], VerificationCustodyBackend: null);
        public string Health() => "Healthy";
        public IReadOnlyList<RunnerSessionDto> List() => [];
        public Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            throw new KeyNotFoundException();
        public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct)
        {
            Mutations.Add("start");
            Owned++;
            return Task.FromResult(new RunnerSessionDto(request.SessionId, 1, DateTime.UtcNow, "Running", null, "", 0));
        }
        public RunnerBufferDto GetBuffer(Guid sessionId) => new(sessionId, "", 0);
        public RunnerSnapshotDto GetSnapshot(Guid sessionId) => new(sessionId, "", "", 0, DateTime.UtcNow);
        public RunnerTranscriptDto GetTranscript(Guid sessionId) => new(sessionId, [], 0);
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
        {
            Mutations.Add("input");
            return Task.CompletedTask;
        }
        public Task<RunnerConditionalInputResult> SendConditionalInputAsync(Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct) =>
            Task.FromResult(new RunnerConditionalInputResult(sessionId, ConditionalInputOutcomes.Unsupported, null, null));
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<RunnerKillGenerationResult> KillGenerationAsync(Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct) =>
            Task.FromResult(new RunnerKillGenerationResult(sessionId, false, KillGenerationOutcomes.Missing, null));
    }
}
