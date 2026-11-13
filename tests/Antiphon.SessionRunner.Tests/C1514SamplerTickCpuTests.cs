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
        var time = TimeProvider.System;
        using var sampler = new HostStatsSamplerService(new BlockingProbe(TimeSpan.FromMilliseconds(120)),
            new HostStatsStore(new HostStatsSettings(), time), new NoCpu(), () => [], _ => null, time,
            Options.Create(new HostStatsSettings { ProcessSampling = false }), logger);
        await sampler.SampleOnceAsync(logTick: false);
        await sampler.SampleOnceAsync();
        var tick = logger.Ticks.ShouldHaveSingleItem();
        // The probe slept 120 ms without running. Wall time would log >= 120; the thread's own CPU is near zero.
        tick.ShouldBeLessThan(25d, "HOST_LOAD_TICK must charge thread CPU, not descheduled wall time, on " + Environment.OSVersion.Platform);
        tick.ShouldBeGreaterThanOrEqualTo(0d);
    }

    [Test]
    public void ThreadCpuCountsWorkTheThreadDoes()
    {
        var start = HostStatsSamplerService.ThreadCpuMilliseconds().ShouldNotBeNull("a thread CPU clock exists on Linux and Windows");
        var wall = System.Diagnostics.Stopwatch.StartNew();
        var spin = 0L;
        while (wall.ElapsedMilliseconds < 60) spin++;
        var used = HostStatsSamplerService.ThreadCpuMilliseconds().ShouldNotBeNull() - start;
        // A spinning thread charges real CPU: never more than the wall it ran, and not zero.
        used.ShouldBeGreaterThan(0d, "spin=" + spin);
        used.ShouldBeLessThanOrEqualTo(wall.Elapsed.TotalMilliseconds + 5d);
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
