using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-1681 mutation survivors PC-19a/PC-19c. The kernel closes a dying root's descriptors in
/// do_exit, before the runner's wait returns, so the master can read EIO before the exit is
/// recorded. The hangup decision then rests on the root's own /proc/&lt;pid&gt;/stat. These
/// cases hold <see cref="PtyAgentRunner.ExitHandledForTest"/>, which runs before the exit is
/// recorded, while the explicit release drains the output.
/// </summary>
[NativeLinuxOnly("CARD-1681 requires the real Linux Porta PTY hangup and /proc/<pid>/stat")]
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class Card1681PtyHangupRunnerTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Red if the hangup check gets no stat reader (PC-19a) or reads another process's stat
    /// (PC-19c): the EIO is then a drain failure, so a valid release would not drain.
    /// </summary>
    [Test]
    public async Task C1681_Eio_before_exit_observed_drains_from_proc_stat()
    {
        var hold = new ExitHold();
        var containment = new EmptyContainment();
        await using var runner = new PtyAgentRunner
        {
            CustodyContainment = containment,
            ExitHandledForTest = hold.Hold,
        };
        try
        {
            var ready = "READY_" + Guid.NewGuid().ToString("N");
            var tail = "TAIL_" + Guid.NewGuid().ToString("N");
            await runner.StartTrackedAsync("/bin/sh",
                ["-c", $"printf '{ready}\\n'; read release; printf '{tail}\\n'; exit 0"],
                AppContext.BaseDirectory, new Dictionary<string, string>(), 80, 24, 0, new Journal());
            (await runner.WaitForOutputAsync(text => text.Contains(ready, StringComparison.Ordinal), Budget))
                .ShouldBeTrue("root ready");
            await runner.WriteAsync("go\n");

            await hold.Entered.WaitAsync(Budget);
            runner.Exited.IsCompleted.ShouldBeFalse("the exit is held before it is recorded");

            var observation = await runner.SealAndObserveCustodyAsync().WaitAsync(Budget);
            observation.ShouldBe(new PtyCustodyObservation(0, true));
            runner.Exited.IsCompleted.ShouldBeFalse("the drain decided from /proc/<pid>/stat alone");
            runner.SnapshotText().ShouldContain(tail);
            containment.Terminations.ShouldBe(1);
        }
        finally
        {
            hold.Release();
        }

        (await runner.Exited.WaitAsync(Budget)).ShouldBe(0);
    }

    /// <summary>
    /// The guard side, with the same hold installed: a live root that closes every slave
    /// descriptor makes the master read EIO, and its stat shows it alive, so the drain still
    /// fails. Red if the reader reads a missing path (any gone pid) or every EIO ends output.
    /// </summary>
    [Test]
    public async Task C1681_Eio_while_root_alive_with_exit_held_stays_drain_failure()
    {
        var hold = new ExitHold();
        var dir = Path.Combine(Path.GetTempPath(), "c1681-alive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var detached = Path.Combine(dir, "detached");
        var exit = Path.Combine(dir, "exit");
        await using var runner = new PtyAgentRunner
        {
            CustodyContainment = new EmptyContainment(),
            ExitHandledForTest = hold.Hold,
        };
        try
        {
            var ready = "READY_" + Guid.NewGuid().ToString("N");
            await runner.StartTrackedAsync("/bin/sh",
                ["-c", $"printf '{ready}\\n'; read release; exec </dev/null >/dev/null 2>&1; : > '{detached}'; "
                    + $"while [ ! -e '{exit}' ]; do /bin/sleep 0.05; done; exit 0"],
                AppContext.BaseDirectory, new Dictionary<string, string>(), 80, 24, 0, new Journal());
            (await runner.WaitForOutputAsync(text => text.Contains(ready, StringComparison.Ordinal), Budget))
                .ShouldBeTrue("root ready");
            await runner.WriteAsync("go\n");
            var deadline = DateTime.UtcNow + Budget;
            while (!File.Exists(detached) && DateTime.UtcNow < deadline)
                await Task.Delay(50);
            File.Exists(detached).ShouldBeTrue("root closed its slave descriptors");

            var failure = await Should.ThrowAsync<IOException>(
                () => runner.SealAndObserveCustodyAsync().WaitAsync(Budget));
            failure.Message.ShouldBe("verification_custody_output_drain_failed");
            failure.InnerException.ShouldBeOfType<IOException>().HResult.ShouldBe(5);
            hold.Entered.IsCompleted.ShouldBeFalse("the root is still alive");
            runner.Exited.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            File.WriteAllText(exit, "go");
            hold.Release();
        }

        await hold.Entered.WaitAsync(Budget);
        (await runner.Exited.WaitAsync(Budget)).ShouldBe(0);
        Directory.Delete(dir, recursive: true);
    }

    /// <summary>Blocks the exit handler, bounded, until the test releases it.</summary>
    private sealed class ExitHold
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _release = new();
        public Task Entered => _entered.Task;
        public void Hold()
        {
            _entered.TrySetResult();
            _release.Wait(TimeSpan.FromSeconds(30));
        }
        public void Release() => _release.Set();
    }

    /// <summary>A Linux tree that is empty at every read; the root is not placed in it.</summary>
    private sealed class EmptyContainment : IPtyCustodyContainment
    {
        public Guid ContainerId { get; } = Guid.NewGuid();
        public int Terminations { get; private set; }
        public PtyTrackedLaunch Place(PtyTrackedLaunch launch) => launch;
        public PtyCustodyPopulation ReadActive() => new(0, []);
        public PtyTerminationObservation Terminate()
        {
            Terminations++;
            return new(true, 0);
        }
    }

    private sealed class Journal : IPtyCustodyJournal
    {
        public Guid ContainerId { get; } = Guid.NewGuid();
        public void RecordStartIntent() { }
        public void RecordTracking(int processId) { }
        public void RecordSeal() { }
    }
}
