using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0649. A runner-delivered multi-line prompt whose first line is the task marker must
/// reach the pty intact. The server2 loss was upstream of this write (the queue replaced the
/// brief with a pointer that omitted the marker). These pins are the runner half: the Input
/// and ConditionalInput frames, then the bytes the runtime hands to the pty.
/// </summary>
[NotInParallel("SessionLiveness")]
[ParallelLimiter<ProcessSpawnLimit>]
public class RunnerMultilinePromptDeliveryTests
{
    private const string Marker = "[antiphon-task:903bf8a7]";

    private static string Prompt =>
        Marker + " role=Custom tier=High workspace=Worktree\n\n"
        + "YOUR MESSAGE IS NOT IN THIS MESSAGE.\n"
        + "Read .antiphon/inbox/note.md in full.";

    [Test]
    public async Task Input_and_conditional_input_frames_keep_the_marker_line()
    {
        var runtime = new RecordingPty();
        var dispatcher = new PhoneHomeCommandDispatcher(runtime, new PhoneHomeSettings
        {
            Enabled = true,
            AllowedCwd = "/work",
            Capacity = 2,
        });
        var sessionId = Guid.NewGuid();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);

        var input = await dispatcher.DispatchAsync(Frame(PhoneHomeOperation.Input, new
        {
            sessionId,
            input = Prompt,
        }), CancellationToken.None);
        input.Kind.ShouldBe(PhoneHomeFrameKind.Result);

        var conditional = await dispatcher.DispatchAsync(Frame(PhoneHomeOperation.ConditionalInput, new
        {
            sessionId,
            expectedAcceptedStartedAt = generation,
            expectedLastSequence = 4L,
            input = Prompt,
        }), CancellationToken.None);
        conditional.Kind.ShouldBe(PhoneHomeFrameKind.Result);

        runtime.Writes.Count.ShouldBe(2);
        foreach (var write in runtime.Writes)
        {
            write.ShouldBe(Prompt);
            write.Split('\n')[0].ShouldStartWith(Marker);
            write.ShouldContain("YOUR MESSAGE IS NOT IN THIS MESSAGE.");
            write.ShouldContain("Read .antiphon/inbox/note.md in full.");
        }
    }

    [Test]
    public async Task The_pty_receives_the_marker_line_of_a_multiline_prompt()
    {
        var logRoot = TestSessionLogRoot.Create("c649-prompt");
        await using var runtime = new SessionRunnerRuntime(Options.Create(new SessionRunnerSettings
        {
            SessionLogPath = logRoot,
            PtyHostLingerHours = 0.02,
            CpuWatchdogEnabled = false,
        }), NullLogger<SessionRunnerRuntime>.Instance);
        var sessionId = Guid.NewGuid();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await runtime.StartAsync(new RunnerLaunchRequest(
            sessionId,
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ["/d", "/q", "/k", "@echo off & prompt $G"],
            new Dictionary<string, string>(),
            Path.GetTempPath(),
            80, 24), cts.Token);
        await Task.Delay(300, cts.Token);

        await runtime.SendInputAsync(sessionId, Prompt, cts.Token);

        var write = runtime.SnapshotBackendWrites().Single(w => w.SessionId == sessionId).Input;
        write.ShouldBe(Prompt);
        write.Split('\n')[0].ShouldBe(Marker + " role=Custom tier=High workspace=Worktree");
        write.ShouldContain("YOUR MESSAGE IS NOT IN THIS MESSAGE.");

        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), cts.Token);
    }

    private static PhoneHomeFrame Frame(PhoneHomeOperation operation, object payload) =>
        new(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), operation,
            JsonSerializer.SerializeToElement(payload, PhoneHomeFraming.Json));

    private sealed class RecordingPty : IPhoneHomeRuntimeSurface
    {
        public List<string> Writes { get; } = [];

        public RunnerCapabilitiesDto Capabilities() =>
            new("InboxConhost", "inbox", "test", false, Features: [], VerificationCustodyBackend: null);
        public string Health() => "Healthy";
        public IReadOnlyList<RunnerSessionDto> List() => [];
        public Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(Session(sessionId));
        public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct) =>
            Task.FromResult(Session(request.SessionId));
        public RunnerBufferDto GetBuffer(Guid sessionId) => new(sessionId, "", 0);
        public RunnerSnapshotDto GetSnapshot(Guid sessionId) => new(sessionId, "", "", 0, DateTime.UtcNow);
        public RunnerTranscriptDto GetTranscript(Guid sessionId) => new(sessionId, [], 0);
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
        {
            Writes.Add(input);
            return Task.CompletedTask;
        }
        public Task<RunnerConditionalInputResult> SendConditionalInputAsync(
            Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct)
        {
            Writes.Add(request.Input);
            return Task.FromResult(new RunnerConditionalInputResult(
                sessionId, ConditionalInputOutcomes.Written, request.ExpectedAcceptedStartedAt, request.ExpectedLastSequence));
        }
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<RunnerKillGenerationResult> KillGenerationAsync(
            Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct) =>
            Task.FromResult(new RunnerKillGenerationResult(sessionId, false, KillGenerationOutcomes.Missing, null));
        public int OwnedSessionCount => 0;

        private static RunnerSessionDto Session(Guid id) =>
            new(id, 1, DateTime.UtcNow, "Running", null, "", 0, AcceptedStartedAt: DateTime.UtcNow);
    }
}
