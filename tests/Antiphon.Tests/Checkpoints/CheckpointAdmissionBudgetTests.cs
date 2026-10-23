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
        using var h = new Harness(TempDir());
        var task = h.Start();
        try
        {
            await h.WaitForAcquisition();
            h.Clock.Advance(TimeSpan.FromMinutes(9));
            h.Admit();
            await h.WaitForDriver();
            h.Clock.Advance(TimeSpan.FromMinutes(9));
            h.Release();
            var result = await task;
            result.ExitCode.ShouldBe(0, "nine queued minutes must leave ten execution minutes");
            h.Driver.Starts.Count.ShouldBe(1);
            result.State.Budget!.AdmissionOnlySeconds.ShouldBe(540);
            result.State.Budget.ExecutionSeconds.ShouldBe(540);
        }
        finally { await h.Join(task); }
    }

    [Test]
    public async Task C1531_ready_row_waits_then_executes()
    {
        using var h = new Harness(TempDir());
        h.Slots.Immediate = ordinal => ordinal == 1;
        h.BuildMinutes = 4;
        var task = h.Start(build: true);
        try
        {
            await h.WaitForAcquisition(2);
            h.Clock.Advance(TimeSpan.FromMinutes(9));
            h.Admit(2);
            await h.WaitForDriver();
            h.Clock.Advance(TimeSpan.FromMinutes(5));
            h.Release();
            var result = await task;
            result.ExitCode.ShouldBe(0);
            result.State.Builds.Single().State.ShouldBe("ok");
            h.Driver.Count(CheckpointFixtures.IsBuild).ShouldBe(1);
            h.Driver.Count(CheckpointFixtures.IsRun).ShouldBe(1);
            result.State.Budget!.ExecutionSeconds.ShouldBe(540);
            result.State.Budget.AdmissionOnlySeconds.ShouldBe(540);
        }
        finally { await h.Join(task); }
    }

    [Test]
    public async Task C1531_overlapping_waits_receive_union_credit()
    {
        using var h = new Harness(TempDir());
        var task = h.Start(rows: 2);
        try
        {
            await h.WaitForAcquisition(2);
            h.Clock.Advance(TimeSpan.FromMinutes(7));
            h.Admit(1); h.Admit(2);
            await CheckpointFixtures.WaitUntil(() => h.Driver.Starts.Count == 2);
            h.Release();
            var result = await task;
            result.ExitCode.ShouldBe(0);
            result.State.Budget!.AdmissionOnlySeconds.ShouldBe(420, "the overlapping waits count once");
            result.State.Budget.ExecutionSeconds.ShouldBe(0);
            result.Rows.Sum(row => row.WaitedSeconds).ShouldBe(840);
        }
        finally { await h.Join(task); }
    }

    [Test]
    public async Task C1531_running_sibling_keeps_charging()
    {
        using var h = new Harness(TempDir());
        h.Slots.Immediate = ordinal => ordinal == 1;
        var task = h.Start(rows: 2);
        try
        {
            await h.WaitForAcquisition(2, idle: false);
            await h.WaitForDriver();
            h.Clock.Advance(TimeSpan.FromMinutes(10));
            var result = await task;
            result.ExitCode.ShouldBe(5);
            result.State.Reason.ShouldBe("execution-budget-exceeded");
            result.State.Budget!.AdmissionOnlySeconds.ShouldBe(0);
            h.Driver.Starts.Count.ShouldBe(1);
            h.Driver.InFlight.ShouldBe(0);
        }
        finally { await h.Join(task); }
    }

    [Test]
    public async Task C1531_stuck_admission_is_bounded()
    {
        using var h = new Harness(TempDir());
        var task = h.Start(slotMinutes: 2);
        try
        {
            await h.WaitForAcquisition();
            h.Clock.Advance(TimeSpan.FromMinutes(2));
            var result = await task;
            result.ExitCode.ShouldBe(4);
            result.Rows.Single().State.ShouldBe("slot-timeout");
            result.Rows.Single().TerminalReason.ShouldBe("slot-admission-deadline-exceeded");
            result.Rows.Single().WaitedSeconds.ShouldBe(120);
            result.Rows.Single().ExecutedSeconds.ShouldBe(0);
            h.Driver.Starts.ShouldBeEmpty();
            h.Slots.Exited.ShouldBeTrue();
        }
        finally { await h.Join(task); }
    }

    [Test]
    public async Task C1531_hard_wall_bounds_successive_waits()
    {
        using var h = new Harness(TempDir());
        h.Slots.Immediate = ordinal => ordinal == 1;
        h.BuildMinutes = 1;
        var task = h.Start(build: true, hardWall: 5);
        try
        {
            await h.WaitForAcquisition(2);
            h.Clock.Advance(TimeSpan.FromMinutes(4));
            var result = await task;
            result.ExitCode.ShouldBe(5);
            result.State.Reason.ShouldBe("hard-wall-deadline-exceeded");
            result.State.Budget!.ExecutionSeconds.ShouldBe(60);
            result.State.Budget.AdmissionOnlySeconds.ShouldBe(240);
            h.Driver.Count(CheckpointFixtures.IsRun).ShouldBe(0);
        }
        finally { await h.Join(task); }
    }

    [Test]
    public async Task C1531_execution_overrun_keeps_old_limits()
    {
        foreach (var rowDeadline in new[] { 3, 20 })
        {
            using var h = new Harness(TempDir());
            h.Slots.Immediate = _ => true;
            var task = h.Start(rowMinutes: rowDeadline);
            try
            {
                await h.WaitForDriver();
                h.Clock.Advance(TimeSpan.FromMinutes(Math.Min(rowDeadline, 10)));
                var result = await task;
                result.ExitCode.ShouldBe(5);
                result.Rows.Single().TerminalReason.ShouldBe(rowDeadline == 3 ? "row-deadline-exceeded" : "execution-budget-exceeded");
                h.Driver.InFlight.ShouldBe(0);
            }
            finally { await h.Join(task); }
        }
    }

    [Test]
    public async Task C1531_baseline_shares_remaining_budget()
    {
        using var h = new Harness(TempDir());
        using var budget = new RunBudgetClock(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), h.Clock);
        var task = h.Start(budget: budget);
        try
        {
            await h.WaitForAcquisition();
            h.Clock.Advance(TimeSpan.FromMinutes(9)); h.Admit();
            await h.WaitForDriver();
            h.Clock.Advance(TimeSpan.FromMinutes(9)); h.Release();
            (await task).ExitCode.ShouldBe(0);
            budget.RemainingExecution.ShouldBe(TimeSpan.FromMinutes(1));
            var slots = new QueuedSlots();
            var baseline = new BaselineComparer(h.Driver, slots, TimeSpan.FromMinutes(10), budget: budget);
            var compare = baseline.CompareAsync(TempDir(), TempDir(), "origin/master",
                [new ReportRow { Id = "CP-1", Failures = [new ReportFailure { Name = "N.C.failed" }] }], [], h.Stop.Token);
            await slots.Entered.Task.WaitAsync(h.Stop.Token);
            h.Clock.Advance(TimeSpan.FromMinutes(12));
            await Should.ThrowAsync<OperationCanceledException>(async () => await compare);
            h.Driver.Starts.Count.ShouldBe(1, "expired baseline admission must not launch a fresh driver or cleanup");
        }
        finally { await h.Join(task); }
    }

    [Test]
    public async Task C1531_cancellation_joins_and_keeps_partial_wait()
    {
        using var h = new Harness(TempDir());
        var task = h.Start(owner: true);
        try
        {
            await h.WaitForAcquisition();
            h.Clock.Advance(TimeSpan.FromMinutes(3));
            h.Stop.Cancel();
            var result = await task;
            result.ExitCode.ShouldBe(ExitCodes.OwnerEnded);
            result.Rows.Single().WaitedSeconds.ShouldBe(180);
            h.Driver.Starts.ShouldBeEmpty();
            h.Slots.Exited.ShouldBeTrue();
        }
        finally { await h.Join(task); }
    }

    private sealed class Harness : IDisposable
    {
        public ControlledTimeProvider Clock { get; } = new();
        public CancellationTokenSource Stop { get; } = new(TimeSpan.FromSeconds(10));
        public FakeDriver Driver { get; } = new();
        public QueuedSlots Slots { get; } = new();
        public int BuildMinutes { get; set; }
        private readonly string _root;
        private readonly TaskCompletionSource _driver = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Harness(string root)
        {
            _root = root;
            Driver.When(_ => true, async (request, ct) =>
            {
                if (CheckpointFixtures.IsBuild(request))
                {
                    Clock.Advance(TimeSpan.FromMinutes(BuildMinutes));
                    return new DriverResult(0, "", "");
                }
                _driver.TrySetResult();
                await _release.Task.WaitAsync(ct);
                if (CheckpointFixtures.IsRun(request))
                    CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("N.Sample.passes", "Passed"));
                return new DriverResult(0, "", "");
            });
        }
        public Task<SchedulerResult> Start(bool owner = false, int rows = 1, bool build = false,
            int slotMinutes = 20, int? hardWall = null, int rowMinutes = 20, RunBudgetClock? budget = null)
        {
            var manifest = new CheckpointManifest();
            manifest.Build.SlotWaitMinutes = slotMinutes;
            for (var i = 0; i < rows; i++)
                manifest.Checkpoints.Add(new CheckpointSpec { Id = "CP-" + (i + 1), Command = build ? null : "echo proof",
                    Build = build ? "bin-proof" : null, Filter = build ? "/*/*/Sample/*" : null, TimeoutMinutes = rowMinutes });
            if (build)
            {
                manifest.Builds.Add(new BuildSpec { Id = "bin-proof", Project = "tests/Proof", OutputPath = "bin-proof/" });
                Directory.CreateDirectory(Path.Combine(_root, "tests", "Proof"));
            }
            return new RunScheduler(Driver, new FakePlatform(), Clock).RunAsync(new SchedulerRequest
            {
                Manifest = manifest, Rows = manifest.Checkpoints, WorkingDirectory = _root, RunDirectory = _root,
                State = new RunState(), Slots = Slots, TotalTimeout = TimeSpan.FromMinutes(10),
                HardWallTimeout = hardWall is int wall ? TimeSpan.FromMinutes(wall) : null, Budget = budget,
                OwnerBound = owner,
            }, Stop.Token);
        }
        public async Task WaitForAcquisition(int count = 1, bool idle = true)
        {
            await Slots.Entered.Task.WaitAsync(Stop.Token);
            await CheckpointFixtures.WaitUntil(() => Slots.Count >= count);
            if (idle)
                await CheckpointFixtures.WaitUntil(() => Clock.Events.LastOrDefault(e => e.Action == "change")?.DueTime == Timeout.InfiniteTimeSpan);
        }
        public Task WaitForDriver() => _driver.Task.WaitAsync(Stop.Token);
        public void Admit(int ordinal = 1) => Slots.Admit(ordinal);
        public void Release() => _release.TrySetResult();
        public async Task Join(Task task)
        {
            Release(); Stop.Cancel();
            try { await task; } catch (OperationCanceledException) { }
        }
        public void Dispose() => Stop.Dispose();
    }
    private sealed class QueuedSlots : IBuildSlotClient
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource> _grants = new();
        private int _count;
        public int Count => _grants.Count;
        public Func<int, bool> Immediate { get; set; } = _ => false;
        public void Admit(int ordinal) => _grants[ordinal].TrySetResult();
        public bool Exited { get; private set; }
        public Task<SlotSession> ProbeAsync(CancellationToken ct) => Task.FromResult(new SlotSession("enabled", 1));
        public async Task<SlotLease> AcquireAsync(SlotSession session, string label, CancellationToken ct)
        {
            var ordinal = Interlocked.Increment(ref _count);
            var grant = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _grants[ordinal] = grant;
            if (Immediate(ordinal)) grant.TrySetResult();
            Entered.TrySetResult();
            try { await grant.Task.WaitAsync(ct); return new SlotLease { State = "granted" }; }
            finally { Exited = true; }
        }
    }
}
