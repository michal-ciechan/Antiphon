using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0589 V-8 (S4). The desktop land verifier takes a host build slot through the session
/// runner client before its <c>dotnet build</c>, builds with the grant's <c>-maxcpucount</c>, and
/// releases it after the test run or a build failure. An unreachable runner never fails a land:
/// the verifier builds unleased at <c>-maxcpucount:4</c> and the observer records the
/// <c>BUILD SLOT unleased</c> line. CARD-1461 option C: a memory-floor refusal that outlasts the
/// default-off <c>Landing:BuildSlotMemoryFloorFallbackMinutes</c> builds unleased at the fallback cpu
/// count, and the default still times out. The process runner is faked, so nothing is built here.
/// </summary>
[Category("Unit")]
public sealed class LandVerificationBuildSlotTests
{
    private static readonly Guid Lease = Guid.Parse("c5890000-0000-4000-8000-0000000000a8");

    [Test]
    public async Task A_grant_is_taken_before_the_build_which_carries_its_cpu_count_and_released_after_the_tests()
    {
        var f = new Fixture();
        f.Client.BuildSlotAcquire = request =>
        {
            f.Timeline.Add("acquire " + request.Label);
            return new RunnerBuildSlotAnswer(new BuildSlotGrant(Lease, 3, 1, 2, DateTime.UtcNow.AddMinutes(90)));
        };

        var result = await f.VerifyAsync("/*/*/SomeTests/*");

        result.Ok.ShouldBeTrue(result.Tail);
        f.Timeline.Select(Head).ShouldBe(["acquire", "dotnet build", "dotnet run", "release"]);
        f.Timeline[0].ShouldBe("acquire land-verify task-worktree");
        f.Commands.Single(c => c[0] == "build").ShouldContain("-maxcpucount:3");
        f.Commands.Single(c => c[0] == "run").ShouldNotContain(a => a.StartsWith("-maxcpucount", StringComparison.Ordinal));
        f.Client.ReleasedBuildSlots.ShouldBe([Lease]);
        f.Observer.Lines.ShouldContain(l => l.StartsWith($"BUILD SLOT granted lease={Lease} waited=", StringComparison.Ordinal) && l.EndsWith(" maxcpucount=3", StringComparison.Ordinal));
        f.Observer.Lines.ShouldContain(l => l.StartsWith($"BUILD SLOT released lease={Lease} held=", StringComparison.Ordinal));
    }

    [Test]
    public async Task The_test_run_reuses_the_capped_build_artifacts()
    {
        var f = new Fixture();
        f.Client.BuildSlotAcquire = _ => new RunnerBuildSlotAnswer(new BuildSlotGrant(Lease, 3, 1, 2, DateTime.UtcNow.AddMinutes(90)));

        var result = await f.VerifyAsync("/*/*/SomeTests/*");

        result.Ok.ShouldBeTrue(result.Tail);
        var build = f.Commands.Single(c => c[0] == "build");
        var run = f.Commands.Single(c => c[0] == "run");
        run.Take(6).ShouldBe(["run", "--project", "tests/Antiphon.Tests", "--no-build", "--artifacts-path", build[Array.IndexOf(build, "--artifacts-path") + 1]]);
    }

    [Test]
    public async Task An_unreachable_answer_at_the_wait_deadline_gets_the_full_grace_before_failing_open()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var client = new FakeSessionRunnerClient();
        var answers = 0;
        client.BuildSlotAcquire = _ =>
        {
            if (Interlocked.Increment(ref answers) == 1)
                time.Advance(TimeSpan.FromMinutes(45));
            return null;
        };
        var lines = new List<string>();
        var gate = new SessionRunnerBuildSlotGate(client, time,
            Options.Create(new LandingSettings { BuildSlotWaitMinutes = 45, BuildSlotUnreachableGraceSeconds = 60 }));

        var pending = gate.AcquireAsync("deadline", lines.Add, CancellationToken.None);
        pending.IsCompleted.ShouldBeFalse("the first unreachable answer starts its own grace at the wait deadline");
        for (var seconds = 5; seconds <= 55; seconds += 5)
        {
            time.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(1);
            pending.IsCompleted.ShouldBeFalse($"the build must stay leased through {seconds}s of unreachable grace");
        }
        time.Advance(TimeSpan.FromSeconds(5));
        var hold = await pending.WaitAsync(TimeSpan.FromSeconds(5));

        hold.Outcome.ShouldBe(BuildSlotHoldOutcome.Unleased);
        hold.MaxCpuCount.ShouldBe(4);
        hold.Waited.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMinutes(46));
        lines.ShouldContain(l => l.StartsWith("BUILD SLOT unleased reason=runner_unreachable maxcpucount=4", StringComparison.Ordinal));
        lines.ShouldNotContain(l => l.StartsWith("BUILD SLOT timeout", StringComparison.Ordinal));
    }

    [Test]
    public async Task A_reachable_busy_runner_times_out_at_the_wait_deadline()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var client = new FakeSessionRunnerClient();
        client.BuildSlotAcquire = _ =>
        {
            time.Advance(TimeSpan.FromMinutes(45));
            return new RunnerBuildSlotAnswer(null, Busy: new BuildSlotBusy(2, 2, 1, 5000));
        };
        var lines = new List<string>();
        var gate = new SessionRunnerBuildSlotGate(client, time,
            Options.Create(new LandingSettings { BuildSlotWaitMinutes = 45, BuildSlotUnreachableGraceSeconds = 60 }));

        var hold = await gate.AcquireAsync("busy", lines.Add, CancellationToken.None);

        hold.Outcome.ShouldBe(BuildSlotHoldOutcome.Timeout);
        lines.ShouldContain(l => l.StartsWith("BUILD SLOT timeout after 45m", StringComparison.Ordinal));
        lines.ShouldNotContain(l => l.StartsWith("BUILD SLOT unleased", StringComparison.Ordinal));
    }

    [Test]
    public async Task The_lease_is_released_after_a_build_failure_and_nothing_else_runs()
    {
        var f = new Fixture { BuildOk = false };
        f.Client.BuildSlotAcquire = _ => new RunnerBuildSlotAnswer(new BuildSlotGrant(Lease, 5, 1, 2, DateTime.UtcNow.AddMinutes(90)));

        var result = await f.VerifyAsync("/*/*/SomeTests/*");

        result.Ok.ShouldBeFalse();
        result.Step.ShouldBe("build");
        f.Timeline.Select(Head).ShouldBe(["dotnet build", "release"]);
        f.Commands.Single().ShouldContain("-maxcpucount:5");
        f.Client.ReleasedBuildSlots.ShouldBe([Lease]);
    }

    [Test]
    public async Task An_unreachable_runner_still_builds_unleased_and_never_fails_the_land()
    {
        var f = new Fixture();
        f.Client.BuildSlotAcquire = request =>
        {
            f.Timeline.Add("acquire " + request.Label);
            return null; // connection refused, or an old runner answering 404 on /build-slots
        };

        var result = await f.VerifyAsync("/*/*/SomeTests/*");

        result.Ok.ShouldBeTrue(result.Tail);
        result.Description.ShouldBe("build OK, tests 4/4");
        f.Timeline.Select(Head).ShouldBe(["acquire", "dotnet build", "dotnet run"]);
        f.Commands.Single(c => c[0] == "build").ShouldContain("-maxcpucount:4");
        f.Client.ReleasedBuildSlots.ShouldBeEmpty();
        f.Observer.Lines.ShouldContain(l => l.StartsWith("BUILD SLOT unleased reason=runner_unreachable maxcpucount=4", StringComparison.Ordinal));
    }

    // CARD-1461 option C: a memory-floor refusal that outlasts Landing:BuildSlotMemoryFloorFallbackMinutes
    // builds unleased at Landing:BuildSlotMemoryFloorFallbackMaxCpuCount. The floor answer's RetryAfterMs
    // is 60 s so each one-minute advance from the test body fires exactly one poll.

    [Test]
    public async Task A_memory_floor_refusal_past_the_fallback_window_builds_unleased_at_the_fallback_cpu_count()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var polls = 0;
        var client = new FakeSessionRunnerClient();
        client.BuildSlotAcquire = _ =>
        {
            Interlocked.Increment(ref polls);
            return Floor();
        };
        var lines = new List<string>();
        var gate = new SessionRunnerBuildSlotGate(client, time, Options.Create(new LandingSettings
        {
            BuildSlotWaitMinutes = 30,
            BuildSlotUnreachableGraceSeconds = 60,
            BuildSlotMemoryFloorFallbackMinutes = 5,
            BuildSlotMemoryFloorFallbackMaxCpuCount = 2,
        }));

        var pending = gate.AcquireAsync("floor", lines.Add, CancellationToken.None);
        for (var minute = 1; minute <= 4; minute++)
        {
            await AdvanceOneMinuteAsync(time, () => Volatile.Read(ref polls));
            pending.IsCompleted.ShouldBeFalse($"the floor must keep the gate waiting through minute {minute} of a 5-minute fallback window");
        }
        time.Advance(TimeSpan.FromMinutes(1));
        var hold = await pending.WaitAsync(TimeSpan.FromSeconds(5));

        hold.Outcome.ShouldBe(BuildSlotHoldOutcome.Unleased);
        hold.MaxCpuCount.ShouldBe(2);
        hold.Waited.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMinutes(5));
        hold.QueuePosition.ShouldBe(1);
        lines.ShouldContain(l => l.StartsWith("BUILD SLOT waiting label=floor reason=memory_floor available=4700MB floor=6144MB", StringComparison.Ordinal));
        lines.Count(l => l == "BUILD SLOT unleased reason=memory_floor_fallback available=4700MB floor=6144MB waited=300s maxcpucount=2").ShouldBe(1);
        lines.ShouldNotContain(l => l.StartsWith("BUILD SLOT timeout", StringComparison.Ordinal));
    }

    [Test]
    public async Task A_memory_floor_fallback_builds_at_the_fallback_cpu_count_and_releases_nothing()
    {
        var f = new Fixture
        {
            Settings = new LandingSettings
            {
                BuildSlotWaitMinutes = 30,
                BuildSlotUnreachableGraceSeconds = 0,
                BuildSlotMemoryFloorFallbackMinutes = 5,
                BuildSlotMemoryFloorFallbackMaxCpuCount = 2,
            },
        };
        var polls = 0;
        f.Client.BuildSlotAcquire = request =>
        {
            Interlocked.Increment(ref polls);
            f.Timeline.Add("acquire " + request.Label);
            return Floor();
        };

        var pending = f.VerifyAsync("/*/*/SomeTests/*");
        for (var minute = 1; minute <= 5; minute++)
            await AdvanceOneMinuteAsync(f.Time, () => Volatile.Read(ref polls));
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));

        result.Ok.ShouldBeTrue(result.Tail);
        result.Description.ShouldBe("build OK, tests 4/4");
        f.Timeline.Select(Head).TakeLast(2).ShouldBe(["dotnet build", "dotnet run"]);
        f.Timeline.ShouldNotContain(e => e.StartsWith("release", StringComparison.Ordinal));
        var build = f.Commands.Single(c => c[0] == "build");
        build.ShouldContain("-maxcpucount:2");
        build.ShouldNotContain("-maxcpucount:4");
        f.Client.ReleasedBuildSlots.ShouldBeEmpty();
        f.Observer.Lines.ShouldContain(l => l.StartsWith("BUILD SLOT unleased reason=memory_floor_fallback", StringComparison.Ordinal));
    }

    [Test]
    public async Task A_busy_answer_restarts_the_memory_floor_fallback_window()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var polls = 0;
        var client = new FakeSessionRunnerClient();
        client.BuildSlotAcquire = _ =>
        {
            // Floor at minutes 0, 1, 2; busy at minute 3 (which restarts the window); floor from minute 4 on.
            var poll = Interlocked.Increment(ref polls);
            return poll == 4
                ? new RunnerBuildSlotAnswer(null, Busy: new BuildSlotBusy(2, 2, 1, 60000))
                : Floor();
        };
        var lines = new List<string>();
        var gate = new SessionRunnerBuildSlotGate(client, time, Options.Create(new LandingSettings
        {
            BuildSlotWaitMinutes = 30,
            BuildSlotUnreachableGraceSeconds = 60,
            BuildSlotMemoryFloorFallbackMinutes = 3,
            BuildSlotMemoryFloorFallbackMaxCpuCount = 2,
        }));

        var pending = gate.AcquireAsync("floor", lines.Add, CancellationToken.None);
        for (var minute = 1; minute <= 5; minute++)
        {
            await AdvanceOneMinuteAsync(time, () => Volatile.Read(ref polls));
            pending.IsCompleted.ShouldBeFalse($"the busy answer at minute 3 restarts the 3-minute window, so minute {minute} must still wait");
        }
        time.Advance(TimeSpan.FromMinutes(1));
        var hold = await pending.WaitAsync(TimeSpan.FromSeconds(5));

        hold.Outcome.ShouldBe(BuildSlotHoldOutcome.Unleased);
        hold.MaxCpuCount.ShouldBe(2);
        hold.Waited.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMinutes(6));
        lines.Count(l => l.Contains("reason=memory_floor_fallback", StringComparison.Ordinal)).ShouldBe(1);
        lines.ShouldNotContain(l => l.StartsWith("BUILD SLOT timeout", StringComparison.Ordinal));
    }

    [Test]
    public async Task A_reachable_memory_floor_runner_times_out_at_the_wait_deadline_when_the_fallback_is_off()
    {
        var settings = new LandingSettings();
        settings.BuildSlotMemoryFloorFallbackMinutes.ShouldBe(0, "the shipped default is off");
        settings.BuildSlotMemoryFloorFallbackMaxCpuCount.ShouldBe(2);
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var client = new FakeSessionRunnerClient();
        client.BuildSlotAcquire = _ => Floor();
        var lines = new List<string>();
        var gate = new SessionRunnerBuildSlotGate(client, time, Options.Create(settings));

        var pending = gate.AcquireAsync("floor", lines.Add, CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(30));
        var hold = await pending.WaitAsync(TimeSpan.FromSeconds(5));

        hold.Outcome.ShouldBe(BuildSlotHoldOutcome.Timeout);
        lines.ShouldContain(l => l.StartsWith("BUILD SLOT timeout after 30m position=1", StringComparison.Ordinal));
        lines.ShouldNotContain(l => l.StartsWith("BUILD SLOT unleased", StringComparison.Ordinal));
    }

    private static RunnerBuildSlotAnswer Floor() =>
        new(null, MemoryFloor: new BuildSlotMemoryFloor(4700, 6144, 1, 60000));

    /// <summary>
    /// Advances the fake clock one minute and waits for the poll that fires, so a lagging timer
    /// continuation never merges two minutes into one answer; then yields once more so the gate's
    /// synchronous tail after that answer (its decision and the next delay) has run.
    /// </summary>
    private static async Task AdvanceOneMinuteAsync(FakeTimeProvider time, Func<int> polls)
    {
        var expected = polls() + 1;
        time.Advance(TimeSpan.FromMinutes(1));
        var deadline = Environment.TickCount64 + 5_000;
        while (polls() < expected && Environment.TickCount64 < deadline)
            await Task.Delay(1);
        await Task.Delay(1);
    }

    private static string Head(string entry) =>
        entry.StartsWith("dotnet ", StringComparison.Ordinal) ? string.Join(' ', entry.Split(' ').Take(2)) : entry.Split(' ')[0];

    private sealed class Fixture
    {
        public List<string> Timeline { get; } = [];
        public List<string[]> Commands { get; } = [];
        public FakeSessionRunnerClient Client { get; } = new();
        public RecordingObserver Observer { get; } = new();
        public bool BuildOk { get; init; } = true;
        public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);
        public LandingSettings Settings { get; init; } = new() { BuildSlotWaitMinutes = 30, BuildSlotUnreachableGraceSeconds = 0 };

        public Fixture() => Client.BuildSlotReleased = id => Timeline.Add("release " + id);

        public Task<LandVerification> VerifyAsync(string filter)
        {
            var gate = new SessionRunnerBuildSlotGate(Client, Time, Options.Create(Settings));
            var worktree = Path.Combine(Path.GetTempPath(), "task-worktree");
            return AgentTaskLandService.VerifyWithObserverAsync(worktree, filter, Observer, CancellationToken.None, gate, RunAsync);
        }

        private Task<AgentTaskLandService.ProcessResult> RunAsync(string cwd, ILandingChildObserver? observer, CancellationToken ct, string file, string[] args)
        {
            Commands.Add(args);
            Timeline.Add(file + " " + string.Join(' ', args));
            if (args[0] == "build")
                return Task.FromResult(new AgentTaskLandService.ProcessResult(BuildOk, "", BuildOk ? "" : "error CS0001"));
            var output = args[Array.IndexOf(args, "--results-directory") + 1];
            File.WriteAllText(Path.Combine(output, "landing-verification.trx"),
                """<TestRun><ResultSummary><Counters total="4" executed="4" passed="4" failed="0" /></ResultSummary></TestRun>""");
            return Task.FromResult(new AgentTaskLandService.ProcessResult(true, "", ""));
        }
    }

    private sealed class RecordingObserver : ILandingChildObserver
    {
        public List<string> Lines { get; } = [];
        public Task BeforeStartAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StartedAsync(int processId, long startTicks, CancellationToken ct) => Task.CompletedTask;
        public Task ExitedAsync(CancellationToken ct) => Task.CompletedTask;
        public void Line(string line) => Lines.Add(line);
    }
}
