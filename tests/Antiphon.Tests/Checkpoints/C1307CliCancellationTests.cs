using System.Text.Json;
using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class C1307CliCancellationTests : CheckpointTestBase
{
    [Test]
    public async Task Executor_cancel_persists_130()
    {
        using var cancel = new CancellationTokenSource();
        var entered = Signal();
        var canceled = Signal();
        var release = Signal();
        var slots = new ObservedSlots();
        var driver = new FakeDriver();
        driver.When(_ => true, async (_, token) =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally
            {
                canceled.TrySetResult();
                await release.Task;
            }
            return new DriverResult(0, "", "");
        });
        var run = NewRun();
        var custody = File.ReadAllBytes(Path.Combine(run, "ownership.json"));
        var execute = CheckpointApp.ExecuteAsync(run, cancel.Token, Runtime(driver, slots));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancel.Cancel();
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            execute.IsCompleted.ShouldBeFalse();
            new RunStateStore().TryRead(Path.Combine(run, "state.json"))!.Phase.ShouldNotBe("done");
            release.TrySetResult();
            (await execute.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe(130);
            AssertTerminal(run, 130, "caller-requested");
            driver.InFlight.ShouldBe(0);
            slots.Acquires.ShouldBe(1);
            slots.Releases.ShouldBe(1);
            File.ReadAllBytes(Path.Combine(run, "ownership.json")).ShouldBe(custody);
        }
        finally
        {
            cancel.Cancel();
            release.TrySetResult();
            await execute.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private string NewRun()
    {
        var manifest = new CheckpointManifest
        {
            Checkpoints = [new CheckpointSpec { Id = "CP-1", After = ["S3"], Command = "true", EstimatedMinutes = 1 }],
        };
        return CheckpointApp.CreateRun(manifest, new RunRequest { Slots = "off", KeepOutputs = true }, TempDir(),
            new CheckpointApp.Runtime { SourceCapture = _ => CheckpointFixtures.CleanSource() });
    }

    private static CheckpointApp.Runtime Runtime(IDriver driver, IBuildSlotClient slots) => new()
    {
        EnvironmentLookup = _ => null,
        SourceCapture = _ => CheckpointFixtures.CleanSource(),
        Driver = driver,
        Slots = slots,
    };

    private static void AssertTerminal(string run, int exit, string? reason = null)
    {
        var state = new RunStateStore().TryRead(Path.Combine(run, "state.json")).ShouldNotBeNull();
        state.Phase.ShouldBe("done");
        state.ExitCode.ShouldBe(exit);
        var report = JsonSerializer.Deserialize<ReportModel>(File.ReadAllText(Path.Combine(run, "report.json")), ReportWriter.Json)!;
        report.ExitCode.ShouldBe(exit);
        report.Verdict.ShouldBe(exit == 0 ? "GREEN" : "RED");
        if (reason is not null)
        {
            state.Reason.ShouldBe(reason);
            report.Reason.ShouldBe(reason);
        }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class ObservedSlots : IBuildSlotClient
    {
        public int Acquires { get; private set; }
        public int Releases { get; private set; }
        public Task<SlotSession> ProbeAsync(CancellationToken cancellationToken) => Task.FromResult(new SlotSession("enabled", 4));
        public Task<SlotLease> AcquireAsync(SlotSession session, string label, CancellationToken cancellationToken)
        {
            Acquires++;
            return Task.FromResult(new SlotLease
            {
                State = "granted",
                ReleaseAsync = _ => { Releases++; return Task.CompletedTask; },
            });
        }
    }
}
