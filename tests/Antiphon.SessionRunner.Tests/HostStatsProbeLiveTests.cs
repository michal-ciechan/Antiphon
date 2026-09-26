using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0718 V-13. One method per OS; the other <see cref="Skip.Test"/>s with the reason, so the
/// same filter runs on either lane.
/// </summary>
[Category("Integration")]
public class HostStatsProbeLiveTests
{
    [Test]
    public async Task Linux_probe_reads_live_proc()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test("Linux /proc probe; this lane is "
                + (OperatingSystem.IsWindows() ? "Windows" : "another OS")
                + ", where Windows_probe_reads_live_system_times runs.");
            return;
        }

        var probe = new SystemHostStatsProbe(new HostStatsSettings { Volumes = [Path.GetTempPath()] });
        var first = probe.Read();
        first.ShouldNotBeNull();
        var mem = LinuxHostStatsProbe.ParseMemInfo(File.ReadAllLines("/proc/meminfo"));
        first.Value.MemoryTotalBytes.ShouldBe(mem.TotalBytes);
        first.Value.MemoryAvailableBytes.ShouldNotBeNull();
        first.Value.MemoryAvailableBytes!.Value.ShouldBeGreaterThan(0);
        first.Value.MemoryAvailableBytes.Value.ShouldBeLessThanOrEqualTo(first.Value.MemoryTotalBytes!.Value);
        first.Value.Cores.ShouldBe(Environment.ProcessorCount);
        first.Value.CpuPercent.ShouldBeNull();
        first.Value.Load1.ShouldNotBeNull();
        first.Value.ProcessCount.ShouldNotBeNull();
        first.Value.ProcessCount!.Value.ShouldBeGreaterThan(0);
        first.Value.Disks.Count.ShouldBe(1);
        first.Value.Disks[0].FreeBytes.ShouldBeGreaterThan(0);
        first.Value.Disks[0].FreeBytes.ShouldBeLessThanOrEqualTo(first.Value.Disks[0].TotalBytes);

        await Task.Delay(1000);
        var second = probe.Read();
        second.ShouldNotBeNull();
        second.Value.CpuPercent.ShouldNotBeNull();
        second.Value.CpuPercent!.Value.ShouldBeGreaterThanOrEqualTo(0);
        second.Value.CpuPercent.Value.ShouldBeLessThanOrEqualTo(100);
    }

    [Test]
    public async Task Windows_probe_reads_live_system_times()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Windows GetSystemTimes/GlobalMemoryStatusEx probe; this lane is "
                + (OperatingSystem.IsLinux() ? "Linux" : "another OS")
                + ", where Linux_probe_reads_live_proc runs.");
            return;
        }

        var probe = new SystemHostStatsProbe(new HostStatsSettings { Volumes = [Path.GetTempPath()] });
        var first = probe.Read();
        first.ShouldNotBeNull();
        first.Value.MemoryTotalBytes.ShouldNotBeNull();
        first.Value.MemoryTotalBytes!.Value.ShouldBeGreaterThanOrEqualTo(1L << 30);
        first.Value.MemoryAvailableBytes.ShouldNotBeNull();
        first.Value.MemoryAvailableBytes!.Value.ShouldBeLessThanOrEqualTo(first.Value.MemoryTotalBytes.Value);
        first.Value.SwapTotalBytes.ShouldNotBeNull();
        first.Value.SwapTotalBytes!.Value.ShouldBeGreaterThanOrEqualTo(first.Value.MemoryTotalBytes.Value);
        first.Value.Load1.ShouldBeNull();
        first.Value.CpuPercent.ShouldBeNull();
        first.Value.Cores.ShouldBe(Environment.ProcessorCount);

        await Task.Delay(1000);
        var second = probe.Read();
        second.ShouldNotBeNull();
        second.Value.CpuPercent.ShouldNotBeNull();
        second.Value.CpuPercent!.Value.ShouldBeGreaterThanOrEqualTo(0);
        second.Value.CpuPercent.Value.ShouldBeLessThanOrEqualTo(100);
    }
}
