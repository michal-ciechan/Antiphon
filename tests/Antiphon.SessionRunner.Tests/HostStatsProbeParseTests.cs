using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
public class HostStatsProbeParseTests
{
    private const string FirstStat = "cpu 600 0 200 700 100 0 0 0 0 0";
    private const string SecondStat = "cpu 1000 0 400 1300 200 0 100 0 50 0";

    private static readonly string[] MemLines =
    [
        "MemTotal:       132014068 kB",
        "MemFree:         8000000 kB",
        "MemAvailable:    97687336 kB",
        "SwapTotal:         542716 kB",
        "SwapFree:          286400 kB",
    ];

    [Test]
    public void Server2_compose_indexed_volume_environment_keys_bind_through_AddHostStats()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docker-compose.server2-runner.yml")))
            directory = directory.Parent;
        directory.ShouldNotBeNull();

        var entries = File.ReadAllLines(Path.Combine(directory.FullName, "docker-compose.server2-runner.yml"))
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("SessionRunner__HostStats__Volumes", StringComparison.Ordinal))
            .Select(line => line.Split(':', 2))
            .ToDictionary(parts => parts[0], parts => parts[1].Trim().Trim('"'), StringComparer.Ordinal);
        entries.Count.ShouldBe(2);
        entries["SessionRunner__HostStats__Volumes__0"].ShouldBe("/work");
        entries["SessionRunner__HostStats__Volumes__1"].ShouldBe("/state");

        var prefix = $"C718_{Guid.NewGuid():N}_";
        try
        {
            foreach (var (key, value) in entries)
                Environment.SetEnvironmentVariable(prefix + key, value);
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables(prefix).Build();
            var services = new ServiceCollection();
            services.AddHostStats(configuration, startSampler: false);
            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<IOptions<HostStatsSettings>>().Value.Volumes.ShouldBe(["/work", "/state"]);
        }
        finally
        {
            foreach (var key in entries.Keys)
                Environment.SetEnvironmentVariable(prefix + key, null);
        }
    }

    [Test]
    public async Task ProcStat_cpu_percent_is_busy_delta_over_total_delta()
    {
        var first = LinuxHostStatsProbe.ParseProcStat(FirstStat);
        var second = LinuxHostStatsProbe.ParseProcStat(SecondStat);
        first.ShouldNotBeNull();
        second.ShouldNotBeNull();
        var percent = LinuxHostStatsProbe.CpuPercent(first.Value, second.Value);
        percent.ShouldNotBeNull();
        percent.Value.ShouldBe(50.0, 0.001);
        await Task.CompletedTask;
    }

    [Test]
    public async Task ProcStat_short_first_line_still_parses()
    {
        LinuxHostStatsProbe.ProcStatTotals? eight = null;
        Should.NotThrow(() => eight = LinuxHostStatsProbe.ParseProcStat("cpu 600 0 200 700 100 0 0 0"));
        var ten = LinuxHostStatsProbe.ParseProcStat("cpu 600 0 200 700 100 0 0 0 0 0");
        eight.ShouldBe(ten);
        await Task.CompletedTask;
    }

    [Test]
    public async Task MemInfo_yields_total_available_and_swap()
    {
        var parsed = LinuxHostStatsProbe.ParseMemInfo(MemLines);
        parsed.TotalBytes.ShouldBe(132_014_068L * 1024);
        parsed.AvailableBytes.ShouldBe(97_687_336L * 1024);
        parsed.SwapTotalBytes.ShouldBe(542_716L * 1024);
        parsed.SwapFreeBytes.ShouldBe(286_400L * 1024);
        await Task.CompletedTask;
    }

    [Test]
    public async Task MemInfo_without_MemAvailable_yields_null_available()
    {
        var parsed = LinuxHostStatsProbe.ParseMemInfo(
        [
            "MemTotal: 100 kB",
            "MemFree: 40 kB",
            "SwapTotal: 10 kB",
            "SwapFree: 4 kB",
        ]);
        parsed.AvailableBytes.ShouldBeNull();
        parsed.TotalBytes.ShouldBe(100L * 1024);
        await Task.CompletedTask;
    }

    [Test]
    public async Task LoadAvg_yields_three_loads_and_host_process_count()
    {
        var load = LinuxHostStatsProbe.ParseLoadAvg("28.71 29.75 27.49 13/3789 8436");
        load.ShouldNotBeNull();
        load.Value.Load1.ShouldBe(28.71);
        load.Value.Load5.ShouldBe(29.75);
        load.Value.Load15.ShouldBe(27.49);
        load.Value.ProcessCount.ShouldBe(3789);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Windows_cpu_percent_treats_kernel_as_including_idle()
    {
        var percent = WindowsHostStatsProbe.CpuPercent(0, 0, 0, 600, 800, 200);
        percent.ShouldNotBeNull();
        percent.Value.ShouldBe(40.0, 0.001);
        await Task.CompletedTask;
    }

    [Test]
    public async Task First_read_has_no_cpu_and_second_read_has_the_delta()
    {
        var stats = new Queue<string>([FirstStat, SecondStat]);
        var probe = new LinuxHostStatsProbe(
            path => path switch
            {
                "/proc/stat" => [stats.Dequeue()],
                "/proc/meminfo" => MemLines,
                "/proc/loadavg" => ["1.00 1.00 1.00 1/2 3"],
                _ => throw new IOException(path),
            },
            _ => (10, 20),
            ["/tmp"]);

        var first = probe.Read();
        first.ShouldNotBeNull();
        first.Value.CpuPercent.ShouldBeNull();
        first.Value.MemoryTotalBytes.ShouldNotBeNull();

        var second = probe.Read();
        second.ShouldNotBeNull();
        second.Value.CpuPercent.ShouldNotBeNull();
        second.Value.CpuPercent.Value.ShouldBe(50.0, 0.001);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Linux_probe_memory_floor_and_sample_read_the_same_meminfo()
    {
        var probe = new LinuxHostStatsProbe(_ => MemLines, _ => (1, 2), ["/tmp"]);
        var memory = ((IHostMemoryProbe)probe).AvailableBytes;
        memory.ShouldBe(probe.Read()!.Value.MemoryAvailableBytes);
        memory.ShouldBe(97_687_336L * 1024);

        foreach (var hostStatsFirst in new[] { true, false })
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
            var services = new ServiceCollection();
            if (hostStatsFirst)
            {
                services.AddHostStats(config, startSampler: false);
                services.AddBuildSlotBroker(config);
            }
            else
            {
                services.AddBuildSlotBroker(config);
                services.AddHostStats(config, startSampler: false);
            }

            using var provider = services.BuildServiceProvider();
            var stats = provider.GetRequiredService<IHostStatsProbe>();
            var floor = provider.GetRequiredService<IHostMemoryProbe>();
            floor.ShouldBeSameAs(stats);
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task Platform_switch_selects_the_arm_for_each_os()
    {
        SystemHostStatsProbe.SelectArm(true, false).ShouldBeOfType<WindowsHostStatsProbe>();
        SystemHostStatsProbe.SelectArm(false, true).ShouldBeOfType<LinuxHostStatsProbe>();
        SystemHostStatsProbe.SelectArm(false, false).ShouldBeNull();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Windows_probe_maps_a_failed_GetSystemTimes_to_null()
    {
        var calls = 0;
        var probe = new WindowsHostStatsProbe(
            () =>
            {
                calls++;
                return calls == 1 ? null : (600, 800, 200);
            },
            () => (1UL << 32, 1UL << 30, 2UL << 32, 1UL << 31),
            _ => (10, 20),
            ["/"]);
        var first = probe.Read();
        first.ShouldNotBeNull();
        first.Value.CpuPercent.ShouldBeNull();
        first.Value.MemoryTotalBytes.ShouldBe((long)(1UL << 32));
        // A failed GetSystemTimes must not become a zero baseline, or this read is 40%.
        var secondRead = probe.Read();
        secondRead.ShouldNotBeNull();
        secondRead.Value.CpuPercent.ShouldBeNull();
        secondRead.Value.MemoryTotalBytes.ShouldBe((long)(1UL << 32));

        var failedMemory = new WindowsHostStatsProbe(
            () => (0, 0, 0),
            () => null,
            _ => null,
            ["/"]).Read();
        failedMemory.ShouldNotBeNull();
        failedMemory.Value.MemoryTotalBytes.ShouldBeNull();
        failedMemory.Value.MemoryAvailableBytes.ShouldBeNull();
        failedMemory.Value.SwapTotalBytes.ShouldBeNull();
        failedMemory.Value.SwapFreeBytes.ShouldBeNull();
        await Task.CompletedTask;
    }
}
