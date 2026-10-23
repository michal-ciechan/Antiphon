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
        private readonly string _root;
        private readonly TaskCompletionSource _driver = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Harness(string root)
        {
            _root = root;
            Driver.When(_ => true, async (_, ct) =>
            {
                _driver.TrySetResult();
                await _release.Task.WaitAsync(ct);
                return new DriverResult(0, "", "");
            });
        }
        public Task<SchedulerResult> Start(bool owner = false)
        {
            var row = new CheckpointSpec { Id = "CP-1", Command = "echo proof", TimeoutMinutes = 20 };
            var manifest = new CheckpointManifest { Checkpoints = [row] };
            return new RunScheduler(Driver, new FakePlatform(), Clock).RunAsync(new SchedulerRequest
            {
                Manifest = manifest, Rows = [row], WorkingDirectory = _root, RunDirectory = _root,
                State = new RunState(), Slots = Slots, TotalTimeout = TimeSpan.FromMinutes(10),
                OwnerBound = owner,
            }, Stop.Token);
        }
        public async Task WaitForAcquisition()
        {
            await Slots.Entered.Task.WaitAsync(Stop.Token);
            await Task.Yield();
        }
        public Task WaitForDriver() => _driver.Task.WaitAsync(Stop.Token);
        public void Admit() => Slots.Grant.TrySetResult();
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
        public TaskCompletionSource Grant { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Exited { get; private set; }
        public Task<SlotSession> ProbeAsync(CancellationToken ct) => Task.FromResult(new SlotSession("enabled", 1));
        public async Task<SlotLease> AcquireAsync(SlotSession session, string label, CancellationToken ct)
        {
            Entered.TrySetResult();
            try { await Grant.Task.WaitAsync(ct); return new SlotLease { State = "granted" }; }
            finally { Exited = true; }
        }
    }
}
