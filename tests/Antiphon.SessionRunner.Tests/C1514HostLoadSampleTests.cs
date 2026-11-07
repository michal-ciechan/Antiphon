using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
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
        var hostCores = 24;
        var probe = new LinuxHostStatsProbe(path => path switch
        {
            "/proc/stat" => new[] { reads++ == 0 ? "cpu 0 0 0 0 0 0 0 0" : "cpu 50 0 0 40 10 0 0 0" }
                .Concat(Enumerable.Range(0, hostCores).Select(i => "cpu" + i + " 0 0 0 0 0 0 0 0")).ToArray(),
            "/proc/meminfo" => ["MemTotal: 100 kB", "MemAvailable: 50 kB"],
            "/proc/loadavg" => ["24 12 6 1/10 11"],
            "/sys/fs/cgroup/cpu.max" => ["200000 100000"],
            _ => throw new IOException(),
        }, _ => null, []);
        probe.Read().ShouldNotBeNull().CpuPercent.ShouldBeNull();
        var sample = probe.Read().ShouldNotBeNull();
        sample.CpuPercent.ShouldBe(50d);
        sample.Cores.ShouldBe(24, "preserved:PC-1:host-denominator");
        sample.EffectiveCoreLimit.ShouldBe(2d);
        sample.IoWaitPercent.ShouldBe(10d);
        (sample.CpuPercent * sample.Cores / 100d).ShouldBe(12d);
        (sample.Load1 / sample.Cores).ShouldBe(1d);
        WindowsHostStatsProbe.CpuPercent(0, 0, 0, 600, 800, 200).ShouldBe(40d);
        // A second topology is deliberately different from the executing process. On a
        // 24-core host the first vector alone cannot expose Environment.ProcessorCount.
        hostCores = Environment.ProcessorCount + 1;
        probe.Read().ShouldNotBeNull().Cores.ShouldBe(hostCores, "preserved:PC-1:host-topology");
    }

    [Test]
    public async Task UnknownMetricsStayNull()
    {
        var windows = new WindowsHostStatsProbe(() => (0, 0, 0), () => (1024, 512, 2048, 1024), _ => null, [], () => 24);
        var first = HostLoadProjection.Create(windows.Read().ShouldNotBeNull(),
            new("owned", Guid.NewGuid(), Guid.NewGuid(), 1), new(), HostAdmissionSnapshot.Unknown());
        first.CpuPercent.ShouldBeNull("preserved:PC-2:first-delta");
        first.UnknownReasons["cpuPercent"].ShouldBe("first-delta");
        first.Load1.ShouldBeNull("preserved:PC-3:windows-load"); first.UnknownReasons["load1"].ShouldBe("unsupported");
        var invalid = HostLoadProjection.Create(new ScriptedHostStatsProbe().Read().ShouldNotBeNull() with { CpuPercent = -1 },
            first.Source, new(), HostAdmissionSnapshot.Unknown());
        invalid.CpuPercent.ShouldBeNull(); invalid.UnknownReasons["cpuPercent"].ShouldBe("invalid");
        var time = new SeparateClocks();
        var store = new HostStatsStore(new HostStatsSettings(), time);
        var probe = new ScriptedHostStatsProbe { Cpu = double.NaN };
        using var sampler = Sampler(probe, store, time, new CpuSequence(null, null), []);
        await sampler.SampleOnceAsync();
        store.Latest().ShouldNotBeNull().CpuPercent.ShouldBeNull("preserved:PC-4:nonfinite");
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
        store.Latest().ShouldNotBeNull().Processes.Single().CpuPercent.ShouldBe(50d, "preserved:PC-6:monotonic");
        time.Advance(TimeSpan.FromSeconds(5));
        time.Utc = time.Utc.AddHours(2);
        await sampler.SampleOnceAsync();
        store.Latest().ShouldNotBeNull().Processes.Single().CpuPercent.ShouldBe(50d);
        var cadenceClock = new SeparateClocks(); var cadenceProbe = new CountingProbe();
        using var ticking = Sampler(cadenceProbe, new(new(), cadenceClock), cadenceClock, new CpuSequence(), []);
        await ticking.StartAsync(CancellationToken.None); cadenceProbe.Reads.ShouldBe(1);
        cadenceClock.Utc += TimeSpan.FromHours(3); cadenceProbe.Reads.ShouldBe(1);
        cadenceClock.Advance(TimeSpan.FromMilliseconds(4999)); cadenceProbe.Reads.ShouldBe(1);
        cadenceClock.Advance(TimeSpan.FromMilliseconds(1));
        await C1514HostLoadFixture.EventuallyAsync(() => cadenceProbe.Reads == 2);
        await ticking.StopAsync(CancellationToken.None);
        var disabledProbe = new CountingProbe();
        using var disabled = new HostStatsSamplerService(disabledProbe, new(new(), cadenceClock), new CpuSequence(), () => [], _ => null,
            cadenceClock, Options.Create(new HostStatsSettings { Enabled = false }), NullLogger<HostStatsSamplerService>.Instance);
        await disabled.StartAsync(CancellationToken.None); cadenceClock.Advance(TimeSpan.FromMinutes(1)); disabledProbe.Reads.ShouldBe(0);
        await disabled.StopAsync(CancellationToken.None);
    }

    [Test]
    public void SourceIdentityAndPayloadAreStable()
    {
        var storeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var firstBoot = new HostLoadIdentity("owned-runner", storeId);
        var secondBoot = new HostLoadIdentity("owned-runner", storeId);
        var first = firstBoot.Next(); var second = secondBoot.Next();
        first.Sequence.ShouldBe(1); second.Sequence.ShouldBe(1); first.BootId.ShouldNotBe(second.BootId);
        firstBoot.Next().Sequence.ShouldBe(2);
        var raw = new ScriptedHostStatsProbe().Read().ShouldNotBeNull() with
        { At = C1514HostLoadFixture.T0, Processes = [new("credential-sentinel-do-not-serialize", null, 250, 1024)] };
        var sample = HostLoadProjection.Create(raw, first, new HostStatsSettings(), HostAdmissionSnapshot.Unknown());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(sample, PhoneHomeFraming.Json);
        var retry = JsonSerializer.Deserialize<HostLoadSample>(bytes, PhoneHomeFraming.Json)!;
        retry.Source.RunnerStoreId.ShouldBe(storeId); retry.Source.BootId.ShouldBe(first.BootId);
        retry.CpuPercent.ShouldBe(10d); retry.Processes.Single().CpuPercent.ShouldBe(250d);
        System.Text.Encoding.UTF8.GetString(bytes).ShouldNotContain("credential-sentinel-do-not-serialize");
        JsonSerializer.SerializeToUtf8Bytes(retry, PhoneHomeFraming.Json).ShouldBe(bytes);
    }

    [Test]
    public async Task DisabledHistoryKeepsLegacySampling()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["SessionRunner:HostStats:HistoryEnabled"] = "false" }).Build();
        var services = new ServiceCollection(); var clock = new SeparateClocks();
        services.AddLogging(); services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IHostStatsProbe>(new ScriptedHostStatsProbe());
        services.AddHostStats(config, startSampler: false);
        using var provider = services.BuildServiceProvider();
        var sampler = provider.GetRequiredService<HostStatsSamplerService>();
        var source = provider.GetRequiredService<IHostStatsSource>();
        await sampler.SampleOnceAsync(); source.Snapshot().At.ShouldBe(clock.GetUtcNow());
        var options = provider.GetRequiredService<IOptions<HostStatsSettings>>().Value;
        options.HistoryEnabled.ShouldBeFalse();
        HostStatsRoutes.CapabilityFeatures([], options).ShouldNotContain("hostLoadHistoryV1");
        clock.Advance(TimeSpan.FromSeconds(5)); await sampler.SampleOnceAsync();
        source.Snapshot().At.ShouldBe(clock.GetUtcNow()); source.Series("cpu", "1m").Points.Count.ShouldBe(2);
        options.HistoryEnabled = true;
        clock.Advance(TimeSpan.FromSeconds(5)); await sampler.SampleOnceAsync();
        source.Snapshot().At.ShouldBe(clock.GetUtcNow());
        Should.Throw<IOException>(() => provider.GetRequiredService<IHostLoadHistorySource>().ReadHistory(null, 120));
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
        private readonly FakeTimeProvider _timers = new(C1514HostLoadFixture.T0);
        public DateTimeOffset Utc { get; set; } = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => Utc;
        public override long GetTimestamp() => _timestamp;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => _timers.CreateTimer(callback, state, dueTime, period);
        public void Advance(TimeSpan elapsed) { _timestamp += elapsed.Ticks; Utc += elapsed; _timers.Advance(elapsed); }
    }
    private sealed class CountingProbe : IHostStatsProbe
    {
        public int Reads;
        public HostSample? Read() { Interlocked.Increment(ref Reads); return new ScriptedHostStatsProbe().Read(); }
    }
}
