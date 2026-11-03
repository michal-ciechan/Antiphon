using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using static Antiphon.SessionRunner.Tests.C1677TempSweepTests;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1677 S2 (review 12ba9a25 D-2, portable). The runner's half of operation 47: a TempSweep frame through the
/// real <see cref="PhoneHomeCommandDispatcher"/> and, except for the gate case, the real
/// <see cref="PhoneHomeRuntimeAdapter"/> over a real <see cref="RunnerTempSweep"/> bound to a scratch temp root. The
/// dispatcher refuses a surface that does not advertise tempSweepV1 before any engine; the adapter advertises and
/// answers only on Linux (its OS parameter forced both ways); the sweep never waits on a held mutation lock; DryRun
/// never deletes while Execute does; an unknown mode refuses the whole request.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(20_000)]
public sealed class C1677TempSweepDispatchTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Sweep_is_refused_without_the_capability(bool advertised, CancellationToken ct)
    {
        // A surface that would sweep but does not advertise tempSweepV1 never reaches its sweep: 501, no call.
        // advertised=true is the control: the same surface with the feature is answered.
        var surface = new Surface(advertised ? [RunnerCapabilityFeatures.TempSweepV1] : [RunnerCapabilityFeatures.WorkspaceRetentionV1],
            (_, _) => Task.FromResult(new PhoneHomeTempSweepResponse(TempSweepModes.DryRun, [], [])));
        var reply = await Dispatcher(surface).DispatchAsync(Frame(Request(execute: false)), ct);
        if (advertised)
        {
            reply.Kind.ShouldBe(PhoneHomeFrameKind.Result, reply.ErrorDetail);
            surface.SweepCalls.ShouldBe(1);
            return;
        }
        reply.Kind.ShouldBe(PhoneHomeFrameKind.Error);
        reply.StatusCode.ShouldBe(501);
        reply.ErrorCode.ShouldBe(PhoneHomeProblemTypes.UnsupportedOperation);
        surface.SweepCalls.ShouldBe(0);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Sweep_is_advertised_and_answered_only_on_linux(bool isLinux, CancellationToken ct)
    {
        // The real adapter with a real sweep: on Linux it advertises tempSweepV1 and answers a DryRun; off Linux it
        // advertises nothing and the dispatcher refuses 501. The old entry stays either way (DryRun).
        using var s = new Scratch();
        var name = "c487-probe-" + Hex32();
        var path = s.Entry(name, TimeSpan.FromHours(49));
        await using var runtime = Runtime(s);
        var adapter = new PhoneHomeRuntimeAdapter(runtime, RunnerBuildIdentity.Resolve(), tempSweep: s.Sweep()) { TempSweepIsLinux = isLinux };
        PhoneHomeRuntimeAdapter.TempSweepFeatures(isLinux).Contains(RunnerCapabilityFeatures.TempSweepV1).ShouldBe(isLinux);
        adapter.Capabilities().Features.ShouldNotBeNull().Contains(RunnerCapabilityFeatures.TempSweepV1).ShouldBe(isLinux);

        var reply = await Dispatcher(adapter).DispatchAsync(Frame(Request(execute: false)), ct);
        if (isLinux)
            Single(Read(reply), name).Verdict.ShouldBe(TempSweepCodes.WouldRemove);
        else
        {
            reply.Kind.ShouldBe(PhoneHomeFrameKind.Error);
            reply.StatusCode.ShouldBe(501);
        }
        Directory.EnumerateFiles(path).Count().ShouldBe(3);
    }

    [Test]
    public async Task Sweep_runs_outside_the_mutation_lock(CancellationToken ct)
    {
        // An Input holds the dispatcher's mutation lock; a TempSweep through the real adapter still completes.
        using var s = new Scratch();
        var name = "c1514-" + Hex32();
        s.Entry(name, TimeSpan.FromHours(49));
        await using var runtime = Runtime(s);
        var adapter = new PhoneHomeRuntimeAdapter(runtime, RunnerBuildIdentity.Resolve(), tempSweep: s.Sweep()) { TempSweepIsLinux = true };
        var surface = new Surface(adapter.Capabilities().Features!, adapter.TempSweepAsync)
        {
            InputGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var dispatcher = Dispatcher(surface);
        var input = dispatcher.DispatchAsync(InputFrame(), ct);
        await surface.InputStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);

        var sweep = await dispatcher.DispatchAsync(Frame(Request(execute: false)), ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
        Single(Read(sweep), name).Verdict.ShouldBe(TempSweepCodes.WouldRemove);
        input.IsCompleted.ShouldBeFalse("the Input still holds the lock");

        surface.InputGate.SetResult();
        (await input.WaitAsync(TimeSpan.FromSeconds(5), ct)).Kind.ShouldBe(PhoneHomeFrameKind.Result);
    }

    [Test]
    [Arguments(TempSweepModes.DryRun)]
    [Arguments(TempSweepModes.Execute)]
    public async Task Mode_reaches_the_sweep_and_dry_run_never_deletes(string mode, CancellationToken ct)
    {
        // The frame's Mode reaches the real sweep intact: DryRun reports would_remove and keeps every byte, Execute
        // (the control) removes the same old entry.
        using var s = new Scratch();
        var name = "antiphon-land-verify-" + Hex32();
        var path = s.Entry(name, TimeSpan.FromHours(49));
        await using var runtime = Runtime(s);
        var adapter = new PhoneHomeRuntimeAdapter(runtime, RunnerBuildIdentity.Resolve(), tempSweep: s.Sweep()) { TempSweepIsLinux = true };

        var response = Read(await Dispatcher(adapter).DispatchAsync(Frame(Request(execute: mode == TempSweepModes.Execute)), ct));
        response.Mode.ShouldBe(mode);
        var candidate = Single(response, name);
        if (mode == TempSweepModes.DryRun)
        {
            candidate.Verdict.ShouldBe(TempSweepCodes.WouldRemove);
            Directory.EnumerateFiles(path).Count().ShouldBe(3);
        }
        else
        {
            candidate.Verdict.ShouldBe(TempSweepCodes.Removed, candidate.Detail);
            Directory.Exists(path).ShouldBeFalse();
        }
    }

    [Test]
    [Arguments("Delete")]
    [Arguments("execute")]
    [Arguments("")]
    public async Task Unknown_mode_refuses_the_whole_request(string mode, CancellationToken ct)
    {
        // An unknown (or wrongly cased) mode is request_invalid: no candidate, the old entry intact.
        using var s = new Scratch();
        var path = s.Entry("c1514-" + Hex32(), TimeSpan.FromHours(49));
        await using var runtime = Runtime(s);
        var adapter = new PhoneHomeRuntimeAdapter(runtime, RunnerBuildIdentity.Resolve(), tempSweep: s.Sweep()) { TempSweepIsLinux = true };

        var response = Read(await Dispatcher(adapter).DispatchAsync(Frame(Request() with { Mode = mode }), ct));
        response.Reason.ShouldBe(TempSweepCodes.RequestInvalid);
        response.Complete.ShouldBeFalse();
        response.Candidates.ShouldBeEmpty();
        Directory.EnumerateFiles(path).Count().ShouldBe(3);
    }

    private static SessionRunnerRuntime Runtime(Scratch s) =>
        new(Options.Create(new SessionRunnerSettings { SessionLogPath = s.Root + "/logs" }), NullLogger<SessionRunnerRuntime>.Instance);

    private static PhoneHomeFrame Frame(PhoneHomeTempSweepRequest request) =>
        new(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.TempSweep,
            JsonSerializer.SerializeToElement(request, PhoneHomeFraming.Json));

    private static PhoneHomeFrame InputFrame() =>
        new(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.Input,
            JsonSerializer.SerializeToElement(new { sessionId = Guid.NewGuid(), input = "x" }, PhoneHomeFraming.Json));

    private static PhoneHomeTempSweepResponse Read(PhoneHomeFrame frame)
    {
        frame.Kind.ShouldBe(PhoneHomeFrameKind.Result, frame.ErrorDetail);
        return frame.Payload!.Value.Deserialize<PhoneHomeTempSweepResponse>(PhoneHomeFraming.Json)!;
    }

    private static PhoneHomeCommandDispatcher Dispatcher(IPhoneHomeRuntimeSurface runtime)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "c1677-dispatch-" + Guid.NewGuid().ToString("N"));
        return new PhoneHomeCommandDispatcher(runtime, new PhoneHomeSettings
        {
            Enabled = true, AllowedCwd = scratch.Replace('\\', '/'), Capacity = 2,
            CapacityStatePath = Path.Combine(scratch, "capacity"),
        });
    }

    /// <summary>Advertises the given features and sweeps through the given delegate; an Input can be held at a gate.</summary>
    private sealed class Surface(
        IReadOnlyList<string> features,
        Func<PhoneHomeTempSweepRequest, CancellationToken, Task<PhoneHomeTempSweepResponse>> sweep) : IPhoneHomeRuntimeSurface
    {
        public TaskCompletionSource? InputGate { get; init; }
        public TaskCompletionSource InputStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _sweepCalls;
        public int SweepCalls => Volatile.Read(ref _sweepCalls);

        public Task<PhoneHomeTempSweepResponse> TempSweepAsync(PhoneHomeTempSweepRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref _sweepCalls);
            return sweep(request, ct);
        }

        public RunnerCapabilitiesDto Capabilities() => new("InboxConhost", "inbox", "test", false, Features: features);
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
