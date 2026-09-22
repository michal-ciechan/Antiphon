using System.Net.WebSockets;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0604 D-3. The whole spill path, producer to recipient, with nothing faked in the middle:
/// the desktop stages a body, the real <see cref="PhoneHomeRunnerClient"/> puts it in an Input
/// frame, the frame crosses a real phone-home WebSocket, the real
/// <see cref="PhoneHomeCommandDispatcher"/> receives it and the real
/// <see cref="RunnerWorkspaceService"/> writes the file.
///
/// The defect this class exists for: the client removed the staged body from its dictionary BEFORE
/// awaiting the request. A WebSocket that dropped mid-send therefore destroyed the only copy —
/// nothing was written on the runner, nothing was left in memory, and the retry typed a pointer at
/// a path that did not exist while the prompt told the agent to "read it in full before you do
/// anything else".
/// </summary>
[Category("Integration")]
public sealed class PhoneHomeSpillTransportTests
{
    [Test]
    public async Task Staged_body_reaches_the_runner_and_lands_on_disk()
    {
        using var runner = new RunnerSide();
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        live.DispatchEligible = true;
        peer.Reply = runner.Answer;

        var courier = new RemoteSpillCourier();
        var client = new PhoneHomeRunnerClient(live, courier);
        var sessionId = Guid.NewGuid();
        var body = "the whole brief\n" + new string('b', 4096);
        courier.Stage(sessionId, runner.Cwd, new PhoneHomeInputSpill(RunnerSide.Relative, body));

        await client.SendInputAsync(sessionId, "Read .antiphon/inbox/brief.md in full", CancellationToken.None);

        File.Exists(runner.SpillPath).ShouldBeTrue("the runner wrote the spilled body inside the session's own cwd");
        (await File.ReadAllTextAsync(runner.SpillPath)).ShouldBe(body);
        runner.Inputs.ShouldHaveSingleItem();
        courier.IsStaged(sessionId).ShouldBeFalse("an acknowledged body is cleared, so a retyped pointer does not rewrite it");
    }

    [Test]
    public async Task A_failed_input_frame_leaves_the_body_recoverable()
    {
        using var runner = new RunnerSide();
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        live.DispatchEligible = true;

        // The runner refuses this frame. Nothing is written, and the desktop's send throws.
        peer.Reply = frame => frame.Operation == PhoneHomeOperation.Input
            ? new PhoneHomeFrame(
                PhoneHomeFrameKind.Error, frame.Epoch, frame.RequestId, frame.Operation,
                ErrorCode: "runner_unavailable", ErrorDetail: "the runner dropped the frame", StatusCode: 503)
            : null;

        var courier = new RemoteSpillCourier();
        var client = new PhoneHomeRunnerClient(live, courier);
        var sessionId = Guid.NewGuid();
        var body = "the whole brief\n" + new string('c', 4096);
        courier.Stage(sessionId, runner.Cwd, new PhoneHomeInputSpill(RunnerSide.Relative, body));

        await Should.ThrowAsync<Exception>(() =>
            client.SendInputAsync(sessionId, "Read .antiphon/inbox/brief.md in full", CancellationToken.None));

        File.Exists(runner.SpillPath).ShouldBeFalse("a refused frame writes nothing");
        courier.IsStaged(sessionId)
            .ShouldBeTrue("a frame that failed must not destroy the only copy of the body");

        // And the body is genuinely recoverable, not merely still present: the retry delivers it.
        peer.Reply = runner.Answer;
        await client.SendInputAsync(sessionId, "Read .antiphon/inbox/brief.md in full", CancellationToken.None);
        (await File.ReadAllTextAsync(runner.SpillPath)).ShouldBe(body);
        courier.IsStaged(sessionId).ShouldBeFalse();
    }

    [Test]
    public async Task A_body_staged_during_the_send_survives_the_acknowledgement()
    {
        using var runner = new RunnerSide();
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        live.DispatchEligible = true;

        var courier = new RemoteSpillCourier();
        var client = new PhoneHomeRunnerClient(live, courier);
        var sessionId = Guid.NewGuid();
        var first = "first body\n" + new string('d', 2048);
        var second = "second body\n" + new string('e', 2048);
        courier.Stage(sessionId, runner.Cwd, new PhoneHomeInputSpill(RunnerSide.Relative, first));

        // A newer spill is staged while the first frame is still in flight. Acknowledging the
        // first must clear only the first: taking whatever happens to be there would drop a body
        // that has not been delivered.
        peer.Reply = frame =>
        {
            if (frame.Operation == PhoneHomeOperation.Input)
                courier.Stage(sessionId, runner.Cwd, new PhoneHomeInputSpill(RunnerSide.Relative, second));
            return runner.Answer(frame);
        };

        await client.SendInputAsync(sessionId, "Read the brief", CancellationToken.None);

        courier.TryPeek(sessionId, out var staged).ShouldBeTrue("the newer body was dropped by the older frame's ack");
        staged.Spill.Body.ShouldBe(second);
    }

    /// <summary>The real recipient half: a dispatcher over a real workspace on a scratch root.</summary>
    private sealed class RunnerSide : IDisposable
    {
        public const string Relative = ".antiphon/inbox/brief.md";

        private readonly string _root;
        private readonly PhoneHomeCommandDispatcher _dispatcher;

        public RunnerSide()
        {
            _root = Path.Combine(Path.GetTempPath(), "c604-spill-" + Guid.NewGuid().ToString("N"))
                .Replace('\\', '/');
            Cwd = _root + "/worktrees/task-deadbeef";
            Directory.CreateDirectory(Cwd);
            SpillPath = Path.Combine(Cwd, ".antiphon", "inbox", "brief.md");
            _dispatcher = new PhoneHomeCommandDispatcher(
                new SilentRuntime(this),
                new PhoneHomeSettings { AllowedCwd = _root, RunnerRepository = _root + "/repo" });
        }

        public string Cwd { get; }
        public string SpillPath { get; }
        public List<string> Inputs { get; } = [];

        /// <summary>Answers the peer's frame by running the REAL runner-side dispatcher.</summary>
        public PhoneHomeFrame? Answer(PhoneHomeFrame frame) =>
            frame.Operation == PhoneHomeOperation.Input
                ? _dispatcher.DispatchAsync(frame, CancellationToken.None).GetAwaiter().GetResult()
                : null;

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                    Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // A scratch root the OS is still holding is not a test failure.
            }
        }

        /// <summary>The runner's terminal, reduced to recording what was typed.</summary>
        private sealed class SilentRuntime(RunnerSide owner) : IPhoneHomeRuntimeSurface
        {
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
                owner.Inputs.Add(input);
                return Task.CompletedTask;
            }
            public Task<RunnerConditionalInputResult> SendConditionalInputAsync(
                Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct) =>
                Task.FromResult(new RunnerConditionalInputResult(
                    sessionId, ConditionalInputOutcomes.Written, DateTime.UtcNow, 1));
            public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;
            public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
            public Task<RunnerKillGenerationResult> KillGenerationAsync(
                Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct) =>
                Task.FromResult(new RunnerKillGenerationResult(
                    sessionId, true, KillGenerationOutcomes.Killed, DateTime.UtcNow));
            public int OwnedSessionCount => 0;

            private static RunnerSessionDto Session(Guid id) =>
                new(id, 1, DateTime.UtcNow, "Running", null, "", 0, AcceptedStartedAt: DateTime.UtcNow);
        }
    }
}
