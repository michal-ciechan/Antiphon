using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;
using SkipTestException = TUnit.Core.Exceptions.SkipTestException;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-1685. The Linux custody kill bound at the runner with no host stop recorder. On that
/// direct branch a throwing kill escapes the seal, and it still counts as a failed attempt.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class Card1685LinuxCustodyRunnerTests
{
    /// <summary>
    /// PC-8b. Red if the schedule update is skipped when the direct kill throws: every read at
    /// t=0 would then kill and throw again.
    /// </summary>
    [Test]
    [NativeLinuxOnly("The Linux custody containment runs on Linux.")]
    public async Task C1685_Direct_throwing_termination_still_backs_off()
    {
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("The Linux custody containment runs on Linux.");
        var containment = new ThrowingContainment();
        var clock = new ManualClock();
        await using var runner = new PtyAgentRunner { CustodyContainment = containment };
        runner.UseCustodyClockForTests(clock);
        await runner.StartTrackedAsync("/bin/sh", ["-c", "exit 0"], AppContext.BaseDirectory,
            new Dictionary<string, string>(), 80, 24, 0, new Journal());
        runner.StopSignal.ShouldBeNull();
        (await runner.Exited.WaitAsync(TimeSpan.FromSeconds(10))).ShouldBe(0);

        var first = await Should.ThrowAsync<IOException>(() => runner.SealAndObserveCustodyAsync());
        first.Message.ShouldBe("c1685");
        containment.Terminations.ShouldBe(1);

        for (var read = 0; read < 3; read++)
            (await runner.SealAndObserveCustodyAsync()).ShouldBe(new PtyCustodyObservation(1, false));
        containment.Terminations.ShouldBe(1, "three more reads at t=0");

        clock.Advance(TimeSpan.FromMilliseconds(999));
        (await runner.SealAndObserveCustodyAsync()).ShouldBe(new PtyCustodyObservation(1, false));
        containment.Terminations.ShouldBe(1, "1 ms before backoff 1");

        clock.Advance(TimeSpan.FromMilliseconds(1));
        await Should.ThrowAsync<IOException>(() => runner.SealAndObserveCustodyAsync());
        containment.Terminations.ShouldBe(2, "at backoff 1");
    }

    /// <summary>A retained tree with one survivor whose kill always throws.</summary>
    private sealed class ThrowingContainment : IPtyCustodyContainment
    {
        private readonly List<int> _pids = [424242];
        public Guid ContainerId { get; } = Guid.NewGuid();
        public int Terminations { get; private set; }
        public PtyTrackedLaunch Place(PtyTrackedLaunch launch) => launch;
        public PtyCustodyPopulation ReadActive() => new((uint)_pids.Count, _pids);
        public PtyTerminationObservation Terminate()
        {
            Terminations++;
            throw new IOException("c1685");
        }
    }

    private sealed class Journal : IPtyCustodyJournal
    {
        public Guid ContainerId { get; } = Guid.NewGuid();
        public void RecordStartIntent() { }
        public void RecordTracking(int processId) { }
        public void RecordSeal() { }
    }

    /// <summary>A monotonic clock the test moves by hand; one tick is 100 ns.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
    }
}
