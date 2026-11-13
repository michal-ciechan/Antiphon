using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1514 rc run 97150a0b: the overhead row's 25 ms tick budget is the sampling thread's own CPU.
/// A tick whose thread is descheduled (host load, paging) must not be charged the wall time it waited.
/// Linux had this since 4e2b755d6; Windows still logged wall time and failed at 32.5 ms under load.
/// </summary>
[Category("Unit"), Timeout(10_000)]
public class C1514SamplerTickCpuTests
{
    [Test]
    public async Task TickBudgetChargesThreadCpuNotDescheduledWall()
    {
        var logger = new TickLogger();
        using var sampler = Sampler(TimeSpan.FromMilliseconds(120), logger);
        await sampler.SampleOnceAsync(logTick: false);
        await sampler.SampleOnceAsync();
        var tick = logger.Ticks.ShouldHaveSingleItem();
        // The probe slept 120 ms without running. Wall time would log >= 120; the thread's own CPU is near zero.
        tick.ShouldBeLessThan(25d, "HOST_LOAD_TICK must charge thread CPU, not descheduled wall time, on " + Environment.OSVersion.Platform);
        tick.ShouldBeGreaterThanOrEqualTo(0d);
    }

    /// <summary>
    /// A 60 ms spin must be charged 15..90 ms. Load-robust: a thread clock counts only while the thread is
    /// scheduled, so descheduling can lower the charge but never raise it; the floor still holds with the
    /// spinning thread descheduled for three quarters of the window. The ceiling is 1.5x the wall, room for
    /// a counter that scales with core frequency. A cycles-per-ms rate off by x10 charges at most 6 ms and
    /// one off by /10 charges at least 150 ms once the thread ran 15 ms, so both fail.
    /// </summary>
    [Test]
    public void ThreadCpuCountsWorkTheThreadDoes()
    {
        // Discard one reading: on Windows the first use calibrates (three 2 ms spins). The window below
        // measures only the spin, never the calibration.
        HostStatsSamplerService.ThreadCpuMilliseconds().ShouldNotBeNull("a thread CPU clock exists on Linux and Windows");
        var start = HostStatsSamplerService.ThreadCpuMilliseconds().ShouldNotBeNull();
        var wall = Stopwatch.StartNew();
        var spin = 0L;
        while (wall.ElapsedMilliseconds < 60) spin++;
        var used = HostStatsSamplerService.ThreadCpuMilliseconds().ShouldNotBeNull() - start;
        var wallMs = wall.Elapsed.TotalMilliseconds;
        used.ShouldBeGreaterThan(15d, $"spin={spin} wallMs={wallMs:F1}");
        used.ShouldBeLessThanOrEqualTo(wallMs * 1.5d, $"spin={spin} wallMs={wallMs:F1}");
    }

    /// <summary>A reader that advances 3 cycles per Stopwatch tick while scheduled calibrates to that rate.</summary>
    [Test]
    public void WindowsCalibrationMeasuresTheRunningCycleRate()
    {
        var reader = new ScriptedCycles();
        var clock = new WindowsThreadCycleClock(reader.Read);
        clock.Milliseconds().ShouldNotBeNull();
        var expected = ScriptedCycles.PerTick * (double)Stopwatch.Frequency / 1000d;
        // The reads bracket the spin, so the measured rate is at or a little above the true one, never x10.
        clock.CyclesPerMs.ShouldBeInRange(expected * 0.9d, expected * 1.5d);
    }

    [Test]
    public void WindowsCycleReadFailingAfterCalibrationIsNull()
    {
        var reader = new ScriptedCycles();
        var clock = new WindowsThreadCycleClock(reader.Read);
        clock.Milliseconds().ShouldNotBeNull();
        reader.Mode = CycleMode.Fail;
        clock.Milliseconds().ShouldBeNull("a failed QueryThreadCycleTime is unknown CPU, never a reading");
    }

    [Test]
    public void WindowsCycleReadOfZeroAfterCalibrationIsNull()
    {
        var reader = new ScriptedCycles();
        var clock = new WindowsThreadCycleClock(reader.Read);
        clock.Milliseconds().ShouldNotBeNull();
        reader.Mode = CycleMode.Zero;
        // A running thread has always accrued cycles, so zero is a failed read; charging it would hide CPU.
        clock.Milliseconds().ShouldBeNull("zero cycles from a running thread is a failed read, not zero CPU");
    }

    [Test]
    public async Task TickFallsBackToWallWhenThreadClockFailsAtTickEnd()
        => (await LoggedTickWithEndReading(null)).ShouldBeGreaterThanOrEqualTo(40d);

    [Test]
    public async Task TickFallsBackToWallWhenThreadClockReadsZeroAtTickEnd()
        => (await LoggedTickWithEndReading(0d)).ShouldBeGreaterThanOrEqualTo(40d, "a failed end reading must not be charged as zero or negative CPU");

    [Test]
    public async Task TickFallsBackToWallWhenThreadClockDoesNotAdvance()
        => (await LoggedTickWithEndReading(10d)).ShouldBeGreaterThanOrEqualTo(40d, "a stuck thread clock must not be charged as zero CPU");

    /// <summary>The tick starts at 10 ms of thread CPU, the probe waits 40 ms, then the end reading is <paramref name="end"/>.</summary>
    private static async Task<double> LoggedTickWithEndReading(double? end)
    {
        var readings = new Queue<double?>([5d, 10d, end]);
        var logger = new TickLogger();
        using var sampler = Sampler(TimeSpan.FromMilliseconds(40), logger, () => readings.Dequeue());
        await sampler.SampleOnceAsync(logTick: false);
        await sampler.SampleOnceAsync();
        readings.ShouldBeEmpty();
        return logger.Ticks.ShouldHaveSingleItem();
    }

    private static HostStatsSamplerService Sampler(TimeSpan probeWait, TickLogger logger, Func<double?>? threadCpu = null)
    {
        var time = TimeProvider.System;
        return new HostStatsSamplerService(new BlockingProbe(probeWait),
            new HostStatsStore(new HostStatsSettings(), time), new NoCpu(), () => [], _ => null, time,
            Options.Create(new HostStatsSettings { ProcessSampling = false }), logger, threadCpu: threadCpu);
    }

    private enum CycleMode { Run, Fail, Zero }

    private sealed class ScriptedCycles
    {
        public const ulong PerTick = 3;
        public CycleMode Mode { get; set; }

        public bool Read(out ulong cycles)
        {
            cycles = Mode switch
            {
                CycleMode.Run => (ulong)Stopwatch.GetTimestamp() * PerTick,
                _ => 0,
            };
            return Mode != CycleMode.Fail;
        }
    }

    private sealed class BlockingProbe(TimeSpan wait) : IHostStatsProbe
    {
        public HostSample? Read() { Thread.Sleep(wait); return new ScriptedHostStatsProbe().Read(); }
    }

    private sealed class NoCpu : IProcessCpuProbe
    {
        public TimeSpan? TryGetTotalCpuTime(int pid, DateTime startedAt) => null;
    }

    private sealed class TickLogger : ILogger<HostStatsSamplerService>
    {
        public List<double> Ticks { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var match = Regex.Match(formatter(state, exception), @"HOST_LOAD_TICK elapsedMs=([0-9.E+-]+)");
            if (match.Success) Ticks.Add(double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        }
    }
}
