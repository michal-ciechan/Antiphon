using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointAdmissionBudgetTests : CheckpointTestBase
{
    [Test]
    public async Task C1531_wait_preserves_execution_budget()
    {
        var h = Fixture();
        h.Slots.OnAcquire = (_, ct) => { h.Clock.Advance(TimeSpan.FromMinutes(9)); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; };
        h.Driver.When(_ => true, (r, ct) => h.Finish(r, ct, CheckpointFixtures.IsBuild(r) ? 4 : 5));
        var result = await h.Run();
        result.ExitCode.ShouldBe(0);
        result.State.AdmissionOnlySeconds.ShouldBe(1080); // two independent nine-minute admissions
        result.State.ExecutionSeconds.ShouldBe(540);
        h.Driver.Calls.Count.ShouldBe(2);
        h.Slots.Released.ShouldBe(2);
    }

    [Test]
    public async Task C1531_ready_row_waits_then_executes()
    {
        var h = Fixture();
        h.Slots.OnAcquire = (label, ct) => { if (!label.StartsWith("build:")) h.Clock.Advance(TimeSpan.FromMinutes(9)); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; };
        h.Driver.When(_ => true, (r, ct) => h.Finish(r, ct, CheckpointFixtures.IsBuild(r) ? 4 : 5));
        var result = await h.Run();
        result.ExitCode.ShouldBe(0);
        result.State.Builds.Single().State.ShouldBe("ok");
        result.State.AdmissionOnlySeconds.ShouldBe(540);
        result.State.RemainingExecutionSeconds.ShouldBe(60);
        h.Driver.Count(CheckpointFixtures.IsBuild).ShouldBe(1);
        h.Driver.Count(CheckpointFixtures.IsRun).ShouldBe(1);
    }

    [Test]
    public async Task C1531_overlapping_waits_receive_union_credit()
    {
        var h = Fixture(twoBuilds: true);
        var releaseBuild = Signal(); var releaseRow = Signal(); var both = Signal();
        var waiting = 0;
        h.Slots.OnAcquire = async (label, ct) =>
        {
            if (label is "build:bin-b" || label.StartsWith("CP-1@"))
            {
                if (Interlocked.Increment(ref waiting) == 2) both.TrySetResult();
                await (label.StartsWith("build:") ? releaseBuild.Task : releaseRow.Task).WaitAsync(ct);
            }
        };
        h.Driver.When(_ => true, (r, ct) => h.Finish(r, ct, 0));
        var run = h.Run();
        try
        {
            await both.Task.WaitAsync(h.Watchdog.Token);
            h.Clock.Advance(TimeSpan.FromMinutes(5));
            releaseBuild.TrySetResult();
            await CheckpointFixtures.WaitUntil(() => h.Driver.Count(CheckpointFixtures.IsBuild) == 2);
            // The second build and its row finish immediately; only the first row is still waiting.
            await CheckpointFixtures.WaitUntil(() => h.Driver.Count(CheckpointFixtures.IsRun) == 1);
            h.Clock.Advance(TimeSpan.FromMinutes(2));
            releaseRow.TrySetResult();
            var result = await run;
            result.ExitCode.ShouldBe(0);
            result.State.AdmissionOnlySeconds.ShouldBe(420);
            result.State.ExecutionSeconds.ShouldBe(0);
        }
        finally { releaseBuild.TrySetResult(); releaseRow.TrySetResult(); h.Watchdog.Cancel(); await run; }
        h.Slots.Released.ShouldBe(4);
    }

    [Test]
    public async Task C1531_running_sibling_keeps_charging()
    {
        var h = Fixture(commands: true);
        var executing = Signal(); var waiting = Signal();
        h.Slots.OnAcquire = async (label, ct) => { if (label.StartsWith("CP-2")) { waiting.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); } };
        h.Driver.When(_ => true, async (_, ct) => { executing.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); return new DriverResult(0,"",""); });
        var run = h.Run();
        try
        {
            await Task.WhenAll(executing.Task, waiting.Task).WaitAsync(h.Watchdog.Token);
            h.Clock.Advance(TimeSpan.FromMinutes(10));
            var result = await run;
            result.ExitCode.ShouldBe(5);
            result.State.Reason.ShouldBe("execution-budget-exceeded");
            result.State.AdmissionOnlySeconds.ShouldBe(0);
            result.State.ExecutionSeconds.ShouldBe(600);
            h.Driver.Calls.Count.ShouldBe(1);
        }
        finally { h.Watchdog.Cancel(); await run; }
        h.Driver.InFlight.ShouldBe(0);
    }

    [Test]
    public async Task C1531_stuck_admission_is_bounded()
    {
        var h = Fixture();
        var entered = Signal();
        h.Slots.OnAcquire = async (_, ct) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); };
        var run = h.Run(total: 30, wall: 50, admission: 2);
        try
        {
            await entered.Task.WaitAsync(h.Watchdog.Token);
            h.Clock.Advance(TimeSpan.FromMinutes(2));
            // A short real observation deadline detects a missing virtual admission timer.
            var completed = await Task.WhenAny(run, Task.Delay(200));
            completed.ShouldBe(run, "admission must finish at A, before E or W");
            var result = await run;
            result.ExitCode.ShouldBe(4);
            result.Rows.Single().SlotReason.ShouldBe("slot-admission-deadline-exceeded");
            result.Rows.Single().WaitedSeconds.ShouldBe(120);
            h.Driver.Calls.ShouldBeEmpty();
        }
        finally { h.Watchdog.Cancel(); await run; }
    }

    [Test]
    public async Task C1531_hard_wall_bounds_successive_waits()
    {
        var h = Fixture();
        h.Slots.OnAcquire = (_, ct) => { h.Clock.Advance(TimeSpan.FromMinutes(6)); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; };
        h.Driver.When(_ => true, (r, ct) => h.Finish(r, ct, 0));
        var result = await h.Run(total: 20, wall: 10, admission: 8);
        result.ExitCode.ShouldBe(5);
        result.State.Reason.ShouldBe("hard-wall-deadline-exceeded");
        h.Driver.Count(CheckpointFixtures.IsBuild).ShouldBe(1);
        h.Driver.Count(CheckpointFixtures.IsRun).ShouldBe(0);
        result.Rows.Single().WaitedSeconds.ShouldBeGreaterThan(0);
        h.Slots.Released.ShouldBe(1);
    }

    [Test]
    public async Task C1531_execution_overrun_keeps_old_limits()
    {
        foreach (var build in new[] { true, false })
        {
            var h = Fixture(); var entered = Signal();
            h.Driver.When(r => CheckpointFixtures.IsBuild(r) == build, async (_, ct) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); return new DriverResult(0,"",""); });
            h.Driver.When(_ => true, (r, ct) => h.Finish(r, ct, 0));
            var run = h.Run(total: 30, wall: 50, local: 3);
            try
            {
                await entered.Task.WaitAsync(h.Watchdog.Token);
                h.Clock.Advance(TimeSpan.FromMinutes(3));
                var result = await run;
                result.ExitCode.ShouldBe(5);
                (build ? result.State.Builds.Single().TerminalReason : result.State.Rows.Single().TerminalReason)
                    .ShouldBe(build ? "build-deadline-exceeded" : "row-deadline-exceeded");
            }
            finally { h.Watchdog.Cancel(); await run; }
            h.Driver.InFlight.ShouldBe(0);
        }
        var broken = Fixture();
        broken.Driver.When(CheckpointFixtures.IsBuild, (_,_) => Task.FromResult(new DriverResult(1,"","compiler error")));
        (await broken.Run()).ExitCode.ShouldBe(2);
    }

    [Test]
    public async Task C1531_cancellation_joins_and_keeps_partial_wait()
    {
        foreach (var owner in new[] { false, true })
        {
            var h = Fixture(commands: true); h.Manifest.Checkpoints.RemoveAt(1);
            var entered = Signal(); var disposing = Signal(); var release = Signal();
            h.Slots.OnAcquire = async (_, ct) =>
            {
                entered.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, ct); }
                finally { disposing.TrySetResult(); await release.Task; }
            };
            using var caller = new CancellationTokenSource();
            var run = h.Run(owner: owner, caller: caller.Token);
            try
            {
                await entered.Task.WaitAsync(h.Watchdog.Token);
                h.Clock.Advance(TimeSpan.FromMinutes(2)); caller.Cancel();
                await disposing.Task.WaitAsync(h.Watchdog.Token);
                run.IsCompleted.ShouldBeFalse("terminal evidence must wait for disposal");
                release.TrySetResult();
                var result = await run;
                result.ExitCode.ShouldBe(owner ? 7 : 130);
                result.Rows.Single().WaitedSeconds.ShouldBe(120);
                h.Driver.Calls.ShouldBeEmpty();
            }
            finally { release.TrySetResult(); h.Watchdog.Cancel(); await run; }
        }
    }

    [Test]
    public async Task C1531_baseline_shares_remaining_budget()
    {
        var h = Fixture();
        var run = CheckpointApp.CreateRun(h.Manifest, new RunRequest { TotalTimeoutMinutes = 10, Baseline = "origin/master", KeepOutputs = true }, h.Root,
            new CheckpointApp.Runtime { SourceCapture = _ => CheckpointFixtures.CleanSource() });
        h.Slots.OnAcquire = (label, ct) => { if (label.StartsWith("build:")) h.Clock.Advance(TimeSpan.FromMinutes(9)); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; };
        h.Driver.When(CheckpointFixtures.IsBuild, (r, ct) => h.Finish(r, ct, 4));
        h.Driver.When(CheckpointFixtures.IsRun, (r, ct) => { h.Clock.Advance(TimeSpan.FromMinutes(1)); CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(r), ("ExampleSurfaceTests.bad","Failed")); return Task.FromResult(new DriverResult(1,"","assertion")); });
        h.Driver.When(r => r.FileName == "git", (r, ct) => { h.Clock.Advance(TimeSpan.FromMinutes(6)); ct.ThrowIfCancellationRequested(); return Task.FromResult(new DriverResult(0,"sha", "")); });
        await CheckpointApp.ExecuteAsync(run, h.Watchdog.Token, new CheckpointApp.Runtime
        {
            Driver = h.Driver, Slots = h.Slots, TimeProvider = h.Clock, SchedulerSuspended = h.Slots.Pump, Platform = new FakePlatform(),
            EnvironmentLookup = _ => null, SourceCapture = _ => CheckpointFixtures.CleanSource(),
        });
        h.Driver.Count(CheckpointFixtures.IsRun).ShouldBe(1);
        h.Driver.Count(r => r.FileName == "git").ShouldBe(1, "baseline fetch uses the remaining five execution minutes; no new rev-parse/cleanup allowance");
        h.Driver.InFlight.ShouldBe(0);
    }

    private Harness Fixture(bool twoBuilds = false, bool commands = false) => new(TempDir(), twoBuilds, commands);
    internal static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal sealed class Harness
    {
        public string Root { get; }
        public ControlledTimeProvider Clock { get; } = new();
        public FakeDriver Driver { get; } = new();
        public AdmissionSlots Slots { get; } = new();
        public CheckpointManifest Manifest { get; } = new();
        public CancellationTokenSource Watchdog { get; } = new(TimeSpan.FromSeconds(10));
        public Harness(string root, bool twoBuilds = false, bool commands = false)
        {
            Root = root;
            if (commands)
                Manifest.Checkpoints = [new() { Id="CP-1", Command="fake-one" }, new() { Id="CP-2", Command="fake-two" }];
            else
            {
                Manifest.Builds.Add(new() { Id="bin-a", Project="tests/Antiphon.Tests", OutputPath="bin-a/" });
                Manifest.Checkpoints.Add(CheckpointFixtures.Row("CP-1", "bin-a"));
                if(twoBuilds) { Manifest.Builds.Add(new() { Id="bin-b", Project="tests/Antiphon.Tests", OutputPath="bin-b/" }); Manifest.Checkpoints.Add(CheckpointFixtures.Row("CP-2", "bin-b")); }
                foreach(var b in Manifest.Builds) Directory.CreateDirectory(Path.Combine(root,b.Project));
                foreach(var r in Manifest.Checkpoints) r.Expect=[];
            }
        }
        public Task<DriverResult> Finish(DriverRequest request, CancellationToken ct, int minutes)
        {
            Clock.Advance(TimeSpan.FromMinutes(minutes)); ct.ThrowIfCancellationRequested();
            if(CheckpointFixtures.IsRun(request)) CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("ExampleSurfaceTests.case","Passed"));
            return Task.FromResult(new DriverResult(0,"",""));
        }
        public async Task<SchedulerResult> Run(int total=10,int wall=30,int admission=20,int? local=null,bool owner=false,CancellationToken caller=default)
        {
            Manifest.Build.SlotWaitMinutes=admission;
            using var linked=CancellationTokenSource.CreateLinkedTokenSource(Watchdog.Token,caller);
            return await new RunScheduler(Driver,new FakePlatform(),Clock).RunAsync(new SchedulerRequest
            {
                Manifest=Manifest, Rows=Manifest.Checkpoints, WorkingDirectory=Root, RunDirectory=Root,
                State=new RunState { RunId="c1531", StartedAt=Clock.GetUtcNow() }, Slots=Slots,
                Commit=new string('a',40), ExpectedSourceSha=new string('a',40), Width=2,
                Suspended=Slots.Pump, TotalTimeout=TimeSpan.FromMinutes(total), HardWallTimeout=TimeSpan.FromMinutes(wall),
                RowTimeoutOverride=local is int m ? TimeSpan.FromMinutes(m) : null, OwnerBound=owner,
            },linked.Token);
        }
    }
    internal sealed class AdmissionSlots : IBuildSlotClient
    {
        public Func<string,CancellationToken,Task>? OnAcquire { get; set; }
        public int Released;
        private readonly System.Collections.Concurrent.ConcurrentQueue<TaskCompletionSource> _pending = new();
        public void Pump() { while(_pending.TryDequeue(out var signal)) signal.TrySetResult(); }
        public Task<SlotSession> ProbeAsync(CancellationToken ct) => Task.FromResult(new SlotSession("enabled",4));
        public async Task<SlotLease> AcquireAsync(SlotSession session,string label,CancellationToken ct)
        {
            if (!label.StartsWith("tool-run:")) { var signal = Signal(); _pending.Enqueue(signal); await signal.Task.WaitAsync(ct); }
            if(OnAcquire is not null) await OnAcquire(label,ct);
            return new SlotLease { State="granted",MaxCpuCount=4,ReleaseAsync=_=> { Interlocked.Increment(ref Released); return Task.CompletedTask; } };
        }
    }
}
