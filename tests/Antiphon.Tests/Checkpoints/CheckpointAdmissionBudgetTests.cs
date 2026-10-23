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
        var settled = Signal();
        h.Slots.IdleObserved = () => { if (h.Driver.Count(CheckpointFixtures.IsRun) == 1 && h.Driver.InFlight == 0) settled.TrySetResult(); };
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
            await settled.Task.WaitAsync(h.Watchdog.Token);
            h.Clock.Advance(TimeSpan.FromMinutes(2));
            releaseRow.TrySetResult();
            var result = await run;
            result.ExitCode.ShouldBe(0);
            result.State.AdmissionOnlySeconds.ShouldBe(420);
            result.State.ExecutionSeconds.ShouldBe(0);
        }
        finally { releaseBuild.TrySetResult(); releaseRow.TrySetResult(); h.Watchdog.Cancel(); await run; }
        h.Slots.Released.ShouldBe(4);
        var clock = new ControlledTimeProvider();
        using var budget = new RunBudgetClock(clock,TimeSpan.FromMinutes(10),TimeSpan.FromMinutes(20));
        using var first = budget.EnterActivity();
        using var second = budget.EnterActivity();
        first.Admission(true);second.Admission(true);
        clock.Advance(TimeSpan.FromMinutes(2)); // scheduler has not offered all eligible work
        budget.SchedulerIdle(true);
        clock.Advance(TimeSpan.FromMinutes(5));
        first.Admission(false);
        clock.Advance(TimeSpan.FromMinutes(2)); // source/preflight/settlement work while a sibling waits
        first.Dispose();
        clock.Advance(TimeSpan.FromMinutes(2));
        budget.SchedulerIdle(false);
        clock.Advance(TimeSpan.FromMinutes(1));
        var phases=new RunState();budget.Capture(phases);
        phases.BudgetWallSeconds.ShouldBe(720);
        phases.AdmissionOnlySeconds.ShouldBe(420);
        phases.ExecutionSeconds.ShouldBe(300);

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
        foreach (var grantAtBoundary in new[] { false, true })
        {
            var clock = new ControlledTimeProvider();
            var enteredHttp = Signal();
            var holders = new Holders();
            var handler = new AdmissionHttp(clock, enteredHttp, grantAtBoundary);
            var client = new BuildSlotClient(handler, "http://slots.invalid/build-slots", wait: TimeSpan.FromMinutes(2),
                clock: clock.GetUtcNow, holders: holders, timeProvider: clock);
            using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var acquire = client.AcquireAsync(new SlotSession("enabled", 4), "bounded-http", watchdog.Token);
            try
            {
                await enteredHttp.Task.WaitAsync(watchdog.Token);
                if (!grantAtBoundary) clock.Advance(TimeSpan.FromMinutes(2));
                var lease = await acquire.WaitAsync(watchdog.Token);
                lease.ExitCode.ShouldBe(4); lease.SlotReason.ShouldBe("slot-admission-deadline-exceeded");
                lease.WaitedSeconds.ShouldBe(120);
                await lease.DisposeAsync();
                holders.Disposed.ShouldBe(1);
                handler.Deletes.ShouldBe(grantAtBoundary ? 1 : 0);
            }
            finally { watchdog.Cancel(); try { await acquire; } catch (OperationCanceledException) { } }
        }
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

        var wall = Fixture();
        var wallRun = CheckpointApp.CreateRun(wall.Manifest, new RunRequest { TotalTimeoutMinutes=10, HardWallTimeoutMinutes=17, Baseline="origin/master",KeepOutputs=true },wall.Root,
            new CheckpointApp.Runtime { SourceCapture=_=>CheckpointFixtures.CleanSource() });
        wall.Slots.OnAcquire=(label,ct)=>
        {
            if(label.StartsWith("build:")) wall.Clock.Advance(TimeSpan.FromMinutes(9));
            if(label.StartsWith("tool-run:")) wall.Clock.Advance(TimeSpan.FromMinutes(4));
            ct.ThrowIfCancellationRequested();return Task.CompletedTask;
        };
        wall.Driver.When(CheckpointFixtures.IsBuild,(r,ct)=>wall.Finish(r,ct,4));
        wall.Driver.When(CheckpointFixtures.IsRun,(r,ct)=>
        {
            wall.Clock.Advance(TimeSpan.FromMinutes(1));
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(r),("ExampleSurfaceTests.bad","Failed"));
            return Task.FromResult(new DriverResult(1,"","assertion"));
        });
        (await CheckpointApp.ExecuteAsync(wallRun,wall.Watchdog.Token,new CheckpointApp.Runtime
        {
            Driver=wall.Driver, Slots=wall.Slots,TimeProvider=wall.Clock,SchedulerSuspended=wall.Slots.Pump,
            Platform=new FakePlatform(),EnvironmentLookup=_=>null,SourceCapture=_=>CheckpointFixtures.CleanSource(),
        })).ShouldBe(1, "baseline exhaustion must preserve the primary test failure");
        wall.Driver.Count(r=>r.FileName=="git").ShouldBe(0,"no baseline driver or cleanup launches after W expires in acquisition");
        var state=new RunStateStore().TryRead(Path.Combine(wallRun,"state.json"))!;
        state.Reason.ShouldBe("hard-wall-deadline-exceeded");
        wall.Driver.InFlight.ShouldBe(0);
        var cleanupClock=new ControlledTimeProvider();
        using var cleanupBudget=new RunBudgetClock(cleanupClock,TimeSpan.FromMinutes(10),TimeSpan.FromMinutes(30));
        cleanupClock.Advance(TimeSpan.FromMinutes(6));
        var cleanupDriver=new FakeDriver();
        string? checkout=null;
        cleanupDriver.When(r=>r.Arguments.Contains("rev-parse"),(_,_)=>Task.FromResult(new DriverResult(0,new string('b',40),"")));
        cleanupDriver.When(r=>r.Arguments.Contains("add"),(r,_)=>
        {
            checkout=r.Arguments[3]; Directory.Exists(checkout).ShouldBeFalse();Directory.CreateDirectory(Path.Combine(checkout,"tests","Antiphon.Tests"));
            return Task.FromResult(new DriverResult(0,"",""));
        });
        cleanupDriver.When(CheckpointFixtures.IsBuild,(_,ct)=>
        {
            cleanupClock.Advance(TimeSpan.FromMinutes(5));ct.ThrowIfCancellationRequested();
            return Task.FromResult(new DriverResult(0,"",""));
        });
        var comparer=new BaselineComparer(cleanupDriver,new FixedSlotClient("off"),cleanupBudget.RemainingExecution,null,cleanupBudget,TimeSpan.FromMinutes(2));
        try
        {
            await comparer.CompareAsync(TempDir(),TempDir(),"origin/master",
                [new ReportRow { Id="CP-1",Build="bin-a",Filter="/*/*/ExampleSurfaceTests/*",Failures=[new ReportFailure {Name="ExampleSurfaceTests.bad"}] }],
                [new BuildSpec {Id="bin-a",Project="tests/Antiphon.Tests",OutputPath="bin-a/"}],CancellationToken.None);
            checkout.ShouldNotBeNull();Directory.Exists(checkout).ShouldBeTrue();
            comparer.ToolRuns.ShouldContain(line=>line.Contains("retained canceled workspace",StringComparison.Ordinal));
            cleanupDriver.Count(r=>r.Arguments.Contains("remove")).ShouldBe(0,"cleanup must not get a fresh one-minute deadline");
            cleanupDriver.InFlight.ShouldBe(0);
        }
        finally { if(checkout is not null && Directory.Exists(checkout)) Directory.Delete(checkout,true); }
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
    internal sealed class Holders : ILeaseHolderSource
    {
        public int Disposed;
        public ILeaseHolder Open() => new Holder(this);
        private sealed class Holder(Holders owner) : ILeaseHolder
        {
            public int Pid => 123;
            public string ProcessStartUtc => "2026-09-25T00:00:00Z";
            public ValueTask DisposeAsync() { Interlocked.Increment(ref owner.Disposed); return ValueTask.CompletedTask; }
        }
    }
    internal sealed class AdmissionHttp(ControlledTimeProvider clock, TaskCompletionSource entered, bool grantAtBoundary) : HttpMessageHandler
    {
        public int Deletes;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Delete)
            {
                Interlocked.Increment(ref Deletes);
                return new HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
            }
            if (request.Method == HttpMethod.Get)
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                { Content = new StringContent("{\"enabled\":true,\"maxCpuCount\":4}") };
            entered.TrySetResult();
            if (!grantAtBoundary) await Task.Delay(Timeout.Infinite, ct);
            else clock.Advance(TimeSpan.FromMinutes(2));
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent("{\"leaseId\":\"boundary\",\"maxCpuCount\":4}") };
        }
    }
    internal sealed class AdmissionSlots : IBuildSlotClient
    {
        public Func<string,CancellationToken,Task>? OnAcquire { get; set; }
        public int Released;
        private readonly System.Collections.Concurrent.ConcurrentQueue<TaskCompletionSource> _pending = new();
        public Action? IdleObserved { get; set; }
        public void Pump() { while(_pending.TryDequeue(out var signal)) signal.TrySetResult(); IdleObserved?.Invoke(); }
        public Task<SlotSession> ProbeAsync(CancellationToken ct) => Task.FromResult(new SlotSession("enabled",4));
        public async Task<SlotLease> AcquireAsync(SlotSession session,string label,CancellationToken ct)
        {
            if (!label.StartsWith("tool-run:")) { var signal = Signal(); _pending.Enqueue(signal); await signal.Task.WaitAsync(ct); }
            if(OnAcquire is not null) await OnAcquire(label,ct);
            return new SlotLease { State="granted",MaxCpuCount=4,ReleaseAsync=_=> { Interlocked.Increment(ref Released); return Task.CompletedTask; } };
        }
    }
}
