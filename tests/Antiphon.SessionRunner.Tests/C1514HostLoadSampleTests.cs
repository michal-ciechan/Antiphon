using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit"), Timeout(2_000)]
public class C1514HostLoadSampleTests
{
    [Test]
    public void CpuPercentUsesHostDenominator()
    {
        var reads = 0;
        var probe = new LinuxHostStatsProbe(path => path switch
        {
            "/proc/stat" => new[] { reads++ == 0 ? "cpu 0 0 0 0 0 0 0 0" : "cpu 50 0 0 40 10 0 0 0" }
                .Concat(Enumerable.Range(0, 24).Select(i => "cpu" + i + " 0 0 0 0 0 0 0 0")).ToArray(),
            "/proc/meminfo" => ["MemTotal: 100 kB", "MemAvailable: 50 kB"],
            "/proc/loadavg" => ["24 12 6 1/10 11"],
            _ => throw new IOException(),
        }, _ => null, []);
        probe.Read().ShouldNotBeNull().CpuPercent.ShouldBeNull();
        var sample = probe.Read().ShouldNotBeNull();
        sample.CpuPercent.ShouldBe(50d);
        sample.Cores.ShouldBe(24, "preserved:PC-1:host-denominator");
        (sample.CpuPercent * sample.Cores / 100d).ShouldBe(12d);
        (sample.Load1 / sample.Cores).ShouldBe(1d);
        WindowsHostStatsProbe.CpuPercent(0, 0, 0, 600, 800, 200).ShouldBe(40d);
    }

    [Test]
    public async Task UnknownMetricsStayNull()
    {
        var time = new SeparateClocks();
        var store = new HostStatsStore(new HostStatsSettings(), time);
        var probe = new ScriptedHostStatsProbe { Cpu = double.NaN };
        using var sampler = Sampler(probe, store, time, new CpuSequence(null, null), []);
        await sampler.SampleOnceAsync();
        store.Latest().ShouldNotBeNull().CpuPercent.ShouldBeNull("preserved:PC-6:nonfinite");
        probe.Cpu = double.PositiveInfinity;
        await sampler.SampleOnceAsync();
        store.Latest().ShouldNotBeNull().CpuPercent.ShouldBeNull();
        probe.Cpu = -1;
        await sampler.SampleOnceAsync();
        store.Latest().ShouldNotBeNull().CpuPercent.ShouldBeNull();
        probe.Cpu = 40;
        await sampler.SampleOnceAsync();
        store.Latest().ShouldNotBeNull().CpuPercent.ShouldBe(40d);
    }

    [Test]
    public async Task ProcessCpuDoesNotBecomeHostCpu()
    {
        var time = new SeparateClocks();
        var store = new HostStatsStore(new HostStatsSettings(), time);
        var targets = new List<HostStatsProcessTarget> { new("session", 123, null, time.GetUtcNow().UtcDateTime) };
        using var sampler = Sampler(new ScriptedHostStatsProbe { Cpu = 40 }, store, time,
            new CpuSequence(TimeSpan.Zero, TimeSpan.FromSeconds(12.5), null, TimeSpan.FromSeconds(1)), targets);
        await sampler.SampleOnceAsync();
        time.Advance(TimeSpan.FromSeconds(5));
        await sampler.SampleOnceAsync();
        store.Latest().ShouldNotBeNull().CpuPercent.ShouldBe(40d);
        store.Latest().ShouldNotBeNull().Processes.Single().CpuPercent.ShouldBe(250d);
        time.Advance(TimeSpan.FromSeconds(5));
        await sampler.SampleOnceAsync();
        store.Latest().ShouldNotBeNull().Processes.Single().CpuPercent.ShouldBeNull();
        targets[0] = targets[0] with { StartedAt = targets[0].StartedAt.AddSeconds(1) };
        time.Advance(TimeSpan.FromSeconds(5));
        await sampler.SampleOnceAsync();
        store.Latest().ShouldNotBeNull().Processes.Single().CpuPercent.ShouldBeNull();
    }

    [Test]
    public async Task CadenceAndDeltasUseMonotonicTime()
    {
        var time = new SeparateClocks();
        var store = new HostStatsStore(new HostStatsSettings(), time);
        using var sampler = Sampler(new ScriptedHostStatsProbe(), store, time,
            new CpuSequence(TimeSpan.Zero, TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(5)),
            [new("session", 123, null, time.GetUtcNow().UtcDateTime)]);
        await sampler.SampleOnceAsync();
        time.Advance(TimeSpan.FromSeconds(5));
        time.Utc = time.Utc.AddHours(-1);
        await sampler.SampleOnceAsync();
        store.Latest().ShouldNotBeNull().Processes.Single().CpuPercent.ShouldBe(50d, "preserved:PC-7:monotonic");
        time.Advance(TimeSpan.FromSeconds(5));
        time.Utc = time.Utc.AddHours(2);
        await sampler.SampleOnceAsync();
        store.Latest().ShouldNotBeNull().Processes.Single().CpuPercent.ShouldBe(50d);
    }

    private static HostStatsSamplerService Sampler(IHostStatsProbe probe, HostStatsStore store, TimeProvider time,
        IProcessCpuProbe cpu, IReadOnlyList<HostStatsProcessTarget> targets)
        => new(probe, store, cpu, () => targets, _ => null, time, Options.Create(new HostStatsSettings()),
            NullLogger<HostStatsSamplerService>.Instance);

    private sealed class CpuSequence(params TimeSpan?[] values) : IProcessCpuProbe
    {
        private readonly Queue<TimeSpan?> _values = new(values);
        public TimeSpan? TryGetTotalCpuTime(int pid, DateTime startedAt) => _values.Dequeue();
    }

    private sealed class SeparateClocks : TimeProvider
    {
        public DateTimeOffset Utc { get; set; } = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => Utc;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan elapsed) { _timestamp += elapsed.Ticks; Utc += elapsed; }
    }
}
