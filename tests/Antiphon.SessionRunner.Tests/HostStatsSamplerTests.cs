using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
public class HostStatsSamplerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task SampleOnce_adds_one_sample_at_the_fake_clock_time()
    {
        var time = new FakeTimeProvider(T0);
        var store = new HostStatsStore(new HostStatsSettings(), time);
        var probe = new ScriptedHostStatsProbe();
        var sampler = Sampler(probe, store, time, new HostStatsSettings());
        await sampler.SampleOnceAsync();
        store.Latest().ShouldNotBeNull().At.ShouldBe(time.GetUtcNow());
    }

    [Test]
    public async Task Faulting_probe_leaves_store_unchanged_and_sampler_alive()
    {
        var time = new FakeTimeProvider(T0);
        var store = new HostStatsStore(new HostStatsSettings(), time);
        var probe = new ScriptedHostStatsProbe { Fault = new IOException("stat unreadable") };
        var sampler = Sampler(probe, store, time, new HostStatsSettings());
        await Should.NotThrowAsync(() => sampler.SampleOnceAsync());
        store.Latest().ShouldBeNull();
        await sampler.SampleOnceAsync();
        store.Latest().ShouldNotBeNull();
    }

    [Test]
    public async Task Session_process_cpu_is_delta_over_wall_and_null_sample_is_null()
    {
        var five = await PercentAfter(TimeSpan.FromSeconds(5));
        Math.Round(five, 2).ShouldBe(50.0);

        var six = await PercentAfter(TimeSpan.FromSeconds(6));
        Math.Round(six, 2).ShouldBe(41.67);
    }

    [Test]
    public async Task Disabled_sampler_never_calls_the_probe()
    {
        var time = new FakeTimeProvider(T0);
        var probe = new ScriptedHostStatsProbe();
        var sampler = Sampler(probe, new HostStatsStore(new HostStatsSettings(), time), time, new HostStatsSettings { Enabled = false });
        await sampler.StartAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(20));
        await sampler.StopAsync(CancellationToken.None);
        probe.Calls.ShouldBe(0);
    }

    private static async Task<double> PercentAfter(TimeSpan gap)
    {
        var time = new FakeTimeProvider(T0);
        var store = new HostStatsStore(new HostStatsSettings(), time);
        var cpu = new QueueCpu();
        cpu.Values[4242] = new Queue<TimeSpan?>([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3.5)]);
        cpu.Values[999] = new Queue<TimeSpan?>([null, null]);
        var started = new DateTime(2026, 9, 26, 11, 0, 0, DateTimeKind.Utc);
        var targets = new List<HostStatsProcessTarget>
        {
            new("s4242", 4242, null, started),
            new("s999", 999, null, started),
        };
        var sampler = Sampler(new ScriptedHostStatsProbe { Cpu = 10 }, store, time, new HostStatsSettings(), cpu, () => targets, pid => pid == 4242 ? 8192 : 1);
        await sampler.SampleOnceAsync();
        time.Advance(gap);
        await sampler.SampleOnceAsync();
        var latest = store.Latest().ShouldNotBeNull();
        var charged = latest.Processes.Single(process => process.SessionId == "s4242");
        charged.WorkingSetBytes.ShouldBe(8192);
        charged.CpuPercent.ShouldNotBeNull();
        var missing = latest.Processes.Single(process => process.SessionId == "s999");
        missing.CpuPercent.ShouldBeNull();
        return charged.CpuPercent.Value;
    }

    private static HostStatsSamplerService Sampler(
        ScriptedHostStatsProbe probe,
        HostStatsStore store,
        TimeProvider time,
        HostStatsSettings settings,
        IProcessCpuProbe? cpu = null,
        Func<IReadOnlyList<HostStatsProcessTarget>>? targets = null,
        Func<int, long?>? workingSet = null)
        => new(
            probe,
            store,
            cpu ?? new QueueCpu(),
            targets ?? EmptyTargets,
            workingSet ?? (_ => null),
            time,
            Options.Create(settings),
            NullLogger<HostStatsSamplerService>.Instance);

    private static IReadOnlyList<HostStatsProcessTarget> EmptyTargets() => [];

    private sealed class QueueCpu : IProcessCpuProbe
    {
        public Dictionary<int, Queue<TimeSpan?>> Values { get; } = new();

        public TimeSpan? TryGetTotalCpuTime(int pid, DateTime startedAt)
            => Values.TryGetValue(pid, out var queue) && queue.Count > 0 ? queue.Dequeue() : null;
    }
}
