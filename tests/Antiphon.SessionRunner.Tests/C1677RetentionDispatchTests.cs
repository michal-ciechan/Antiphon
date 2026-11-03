using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1677 S1 CP-1 (portable). The dispatcher owns the phase split (D-6, S-4): phase A inside the
/// mutation lock, phase B after it is released and only for a moved tree. A fake runtime (F-2,
/// Substitute 1) holds each phase at a gate so the lock's state can be observed from an Input frame.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(15_000)]
public sealed class C1677RetentionDispatchTests
{
    [Test]
    public async Task Phase_b_runs_outside_the_mutation_lock(CancellationToken ct)
    {
        // V-14 (G-36). While phase B is held, an Input frame (a mutating op) completes.
        var runtime = new RetainRuntime { Step = Moved() };
        runtime.CompleteGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = Dispatcher(runtime);
        var retain = dispatcher.DispatchAsync(RetainFrame(), ct);
        await runtime.CompleteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);

        var input = await dispatcher.DispatchAsync(InputFrame(), ct).WaitAsync(TimeSpan.FromSeconds(2), ct);
        input.Kind.ShouldBe(PhoneHomeFrameKind.Result, input.ErrorDetail);
        retain.IsCompleted.ShouldBeFalse("phase B is still held");

        runtime.CompleteGate.SetResult();
        var reply = await retain.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Read(reply).Verdict.ShouldBe(WorkspaceRetentionCodes.Removed);
    }

    [Test]
    public async Task Phase_a_waits_for_the_mutation_lock(CancellationToken ct)
    {
        // V-14b (G-37). An Input holding the lock keeps phase A from starting.
        var runtime = new RetainRuntime { Step = Moved() };
        runtime.InputGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = Dispatcher(runtime);
        var input = dispatcher.DispatchAsync(InputFrame(), ct);
        await runtime.InputStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        var retain = dispatcher.DispatchAsync(RetainFrame(), ct);
        await Task.Delay(500, ct);
        runtime.CheckAndMoveStarts.ShouldBe(0, "phase A waits for the lock");

        runtime.InputGate.SetResult();
        (await input.WaitAsync(TimeSpan.FromSeconds(5), ct)).Kind.ShouldBe(PhoneHomeFrameKind.Result);
        var reply = await retain.WaitAsync(TimeSpan.FromSeconds(5), ct);
        runtime.CheckAndMoveStarts.ShouldBe(1);
        Read(reply).Verdict.ShouldBe(WorkspaceRetentionCodes.Removed);
    }

    [Test]
    public async Task Phase_b_runs_only_after_a_move(CancellationToken ct)
    {
        // V-14c (G-38). A final phase-A answer is the reply; phase B is never called.
        var runtime = new RetainRuntime
        {
            Step = new WorkspaceRetainStep(new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.Kept, WorkspaceRetentionCodes.Dirty)),
        };
        var reply = await Dispatcher(runtime).DispatchAsync(RetainFrame(), ct);
        var response = Read(reply);
        response.Verdict.ShouldBe(WorkspaceRetentionCodes.Kept);
        response.Reason.ShouldBe(WorkspaceRetentionCodes.Dirty);
        runtime.CompleteCalls.ShouldBe(0);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Capability_is_advertised_only_on_linux(bool isLinux)
    {
        // V-14d (G-41, D-11).
        var features = PhoneHomeRuntimeAdapter.RetentionFeatures(isLinux);
        if (isLinux)
            features.ShouldContain(RunnerCapabilityFeatures.WorkspaceRetentionV1);
        else
            features.ShouldNotContain(RunnerCapabilityFeatures.WorkspaceRetentionV1);
        await Task.CompletedTask;
    }

    [Test]
    [Arguments("mirror-then-retain")]
    [Arguments("live-session")]
    [Arguments("live-session-subdirectory")]
    [Arguments("window-elapsed")]
    public async Task Recent_use_by_this_runner_keeps_the_tree(string shape, CancellationToken ct)
    {
        // CARD-1677 D-2 (review c238f9ca). A task re-dispatched after the server's reread: the runner's own
        // Mirror (a reuse at the same sha) or Launch in the tree keeps it, over a real engine and real git.
        // window-elapsed is the control: the same mirror older than the window is removed. CARD-1684 R30: a session
        // launched in a subdirectory of the tree keeps it too (the holder scan here is the fake, empty one).
        using var s = C1677RetentionScratch.Create();
        var m = s.Mirror();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-03T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var runtime = new EngineRuntime(s.Engine());
        var settings = new PhoneHomeSettings
        {
            Enabled = true, AllowedCwd = s.Work, Capacity = 2, CapacityStatePath = s.Root + "/capacity",
            StoreIdPath = s.Root + "/runner-store-id", RunnerRepository = s.Repository, RunnerCloneSource = s.OriginGit,
            AllowedCloneSources = [s.OriginGit],
        };
        var dispatcher = new PhoneHomeCommandDispatcher(runtime, settings, time: clock);
        dispatcher.UseWorkspace(s.Service());
        var liveSession = shape.StartsWith("live-session", StringComparison.Ordinal);
        if (liveSession)
        {
            var cwd = shape == "live-session" ? m.Path : m.Path + "/sub";
            Directory.CreateDirectory(cwd);
            var launch = await dispatcher.DispatchAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.Launch,
                JsonSerializer.SerializeToElement(new RunnerLaunchRequest(Guid.NewGuid(), "/bin/sh", [], new Dictionary<string, string>(), cwd, 80, 24),
                    PhoneHomeFraming.Json)), ct);
            launch.Kind.ShouldBe(PhoneHomeFrameKind.Result, launch.ErrorDetail);
            clock.Advance(TimeSpan.FromHours(1));
        }
        else
        {
            var mirror = await dispatcher.DispatchAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.WorkspaceMirror,
                JsonSerializer.SerializeToElement(new PhoneHomeWorkspaceMirrorRequest(m.Branch, m.Head, "task-a1677001"), PhoneHomeFraming.Json)), ct);
            mirror.Kind.ShouldBe(PhoneHomeFrameKind.Result, mirror.ErrorDetail);
            mirror.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorResponse>(PhoneHomeFraming.Json)!.Path.ShouldBe(m.Path, "the mirror reused the tree");
            clock.Advance(shape == "window-elapsed"
                ? TimeSpan.FromMinutes(settings.RetentionRecentUseMinutes) + TimeSpan.FromSeconds(1)
                : TimeSpan.FromMinutes(settings.RetentionRecentUseMinutes) - TimeSpan.FromSeconds(1));
        }

        var before = C1677RetentionScratch.Tree(m.Path);
        var response = Read(await dispatcher.DispatchAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(),
            PhoneHomeOperation.WorkspaceRetain, JsonSerializer.SerializeToElement(C1677RetentionScratch.Request(m), PhoneHomeFraming.Json)), ct));
        if (shape == "window-elapsed")
        {
            response.Verdict.ShouldBe(WorkspaceRetentionCodes.Removed, response.Detail);
            Directory.Exists(m.Path).ShouldBeFalse();
            return;
        }

        response.Verdict.ShouldBe(WorkspaceRetentionCodes.Kept, response.Detail);
        response.Reason.ShouldBe(liveSession ? WorkspaceRetentionCodes.WorkspaceInUse : WorkspaceRetentionCodes.RecentlyMirrored);
        Directory.Exists(m.Path).ShouldBeTrue("the tree stays at its path");
        C1677RetentionScratch.Tree(m.Path).ShouldBe(before);
        s.Registered(m.Path).ShouldBeTrue();
        s.RetiringEntries().ShouldBeEmpty();
        s.Calls("worktree", "move").ShouldBeEmpty();
        runtime.CheckAndMoveStarts.ShouldBe(0, "the guard answers before phase A");
    }

    private static WorkspaceRetainStep Moved() =>
        new(null, "/scratch/retiring/task-a1677001.0badf00d", "/scratch/repos/antiphon",
            new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.Kept));

    private static PhoneHomeWorkspaceRetainResponse Read(PhoneHomeFrame frame)
    {
        frame.Kind.ShouldBe(PhoneHomeFrameKind.Result, frame.ErrorDetail);
        return frame.Payload!.Value.Deserialize<PhoneHomeWorkspaceRetainResponse>(PhoneHomeFraming.Json)!;
    }

    private static PhoneHomeFrame RetainFrame() =>
        new(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.WorkspaceRetain,
            JsonSerializer.SerializeToElement(new PhoneHomeWorkspaceRetainRequest(
                Guid.Parse("a1677001-0000-4000-8000-000000001677"), "/scratch/worktrees/task-a1677001",
                "feat/card-task-a1677001", WorkspaceRetainModes.Execute, Guid.NewGuid()), PhoneHomeFraming.Json));

    private static PhoneHomeFrame InputFrame() =>
        new(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.Input,
            JsonSerializer.SerializeToElement(new { sessionId = Guid.NewGuid(), input = "x" }, PhoneHomeFraming.Json));

    private static PhoneHomeCommandDispatcher Dispatcher(IPhoneHomeRuntimeSurface runtime)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "c1677-dispatch-" + Guid.NewGuid().ToString("N"));
        return new PhoneHomeCommandDispatcher(runtime, new PhoneHomeSettings
        {
            Enabled = true, AllowedCwd = scratch.Replace('\\', '/'), Capacity = 2,
            CapacityStatePath = Path.Combine(scratch, "capacity"),
        });
    }

    /// <summary>CARD-1677 D-2. Advertises retention over a real engine; a Launch is recorded and listed as Running.</summary>
    private sealed class EngineRuntime(RunnerWorkspaceRetention engine) : IPhoneHomeRuntimeSurface
    {
        private readonly List<RunnerSessionDto> _sessions = [];
        private int _checkAndMoveStarts;
        public int CheckAndMoveStarts => Volatile.Read(ref _checkAndMoveStarts);

        public Task<WorkspaceRetainStep> RetainWorkspaceCheckAndMoveAsync(PhoneHomeWorkspaceRetainRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref _checkAndMoveStarts);
            return engine.CheckAndMoveAsync(request, ct);
        }

        public Task<PhoneHomeWorkspaceRetainResponse> RetainWorkspaceCompleteAsync(WorkspaceRetainStep step, CancellationToken ct) =>
            engine.CompleteAsync(step, ct);

        public RunnerCapabilitiesDto Capabilities() =>
            new("InboxConhost", "inbox", "test", false, Features: [RunnerCapabilityFeatures.WorkspaceRetentionV1]);
        public int OwnedSessionCount { get { lock (_sessions) return _sessions.Count; } }
        public string Health() => "Healthy";
        public IReadOnlyList<RunnerSessionDto> List() { lock (_sessions) return _sessions.ToList(); }
        public Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => throw new KeyNotFoundException();

        public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct)
        {
            var session = new RunnerSessionDto(request.SessionId, 4242, DateTime.UtcNow, "Running", null, "", 0);
            lock (_sessions)
                _sessions.Add(session);
            return Task.FromResult(session);
        }

        public RunnerBufferDto GetBuffer(Guid sessionId) => new(sessionId, "", 0);
        public Task<RunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new RunnerSnapshotDto(sessionId, "", "", 0, DateTime.UtcNow));
        public RunnerTranscriptDto GetTranscript(Guid sessionId) => new(sessionId, [], 0);
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => Task.CompletedTask;
        public Task<RunnerConditionalInputResult> SendConditionalInputAsync(Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct) =>
            Task.FromResult(new RunnerConditionalInputResult(sessionId, ConditionalInputOutcomes.Unsupported, null, null));
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<RunnerKillGenerationResult> KillGenerationAsync(Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct) =>
            Task.FromResult(new RunnerKillGenerationResult(sessionId, false, KillGenerationOutcomes.Missing, null));
    }

    /// <summary>F-2. Advertises retention; each phase and Input can be held at a gate.</summary>
    private sealed class RetainRuntime : IPhoneHomeRuntimeSurface
    {
        public WorkspaceRetainStep Step { get; init; } = new(new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.Kept));
        public TaskCompletionSource? CompleteGate { get; set; }
        public TaskCompletionSource? InputGate { get; set; }
        public TaskCompletionSource CompleteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource InputStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _checkAndMoveStarts;
        private int _completeCalls;
        public int CheckAndMoveStarts => Volatile.Read(ref _checkAndMoveStarts);
        public int CompleteCalls => Volatile.Read(ref _completeCalls);

        public Task<WorkspaceRetainStep> RetainWorkspaceCheckAndMoveAsync(PhoneHomeWorkspaceRetainRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref _checkAndMoveStarts);
            return Task.FromResult(Step);
        }

        public async Task<PhoneHomeWorkspaceRetainResponse> RetainWorkspaceCompleteAsync(WorkspaceRetainStep step, CancellationToken ct)
        {
            Interlocked.Increment(ref _completeCalls);
            CompleteStarted.TrySetResult();
            if (CompleteGate is { } gate)
                await gate.Task.WaitAsync(ct);
            return step.Facts! with { Verdict = WorkspaceRetentionCodes.Removed };
        }

        public RunnerCapabilitiesDto Capabilities() =>
            new("InboxConhost", "inbox", "test", false, Features: [RunnerCapabilityFeatures.WorkspaceRetentionV1]);
        public int OwnedSessionCount => 0;
        public string Health() => "Healthy";
        public IReadOnlyList<RunnerSessionDto> List() => [];
        public Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => throw new KeyNotFoundException();
        public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct) => throw new NotSupportedException();
        public RunnerBufferDto GetBuffer(Guid sessionId) => new(sessionId, "", 0);
        public Task<RunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new RunnerSnapshotDto(sessionId, "", "", 0, DateTime.UtcNow));
        public RunnerTranscriptDto GetTranscript(Guid sessionId) => new(sessionId, [], 0);

        public async Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
        {
            InputStarted.TrySetResult();
            if (InputGate is { } gate)
                await gate.Task.WaitAsync(ct);
        }

        public Task<RunnerConditionalInputResult> SendConditionalInputAsync(Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct) =>
            Task.FromResult(new RunnerConditionalInputResult(sessionId, ConditionalInputOutcomes.Unsupported, null, null));
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<RunnerKillGenerationResult> KillGenerationAsync(Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct) =>
            Task.FromResult(new RunnerKillGenerationResult(sessionId, false, KillGenerationOutcomes.Missing, null));
    }
}
