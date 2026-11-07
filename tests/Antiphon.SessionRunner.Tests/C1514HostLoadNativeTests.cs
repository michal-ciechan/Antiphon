using System.Reflection;
using System.Runtime.InteropServices;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
namespace Antiphon.SessionRunner.Tests;

[Category("Integration"), Timeout(10_000)]
[ClassDataSource<C1514HostLoadFixture>(Shared = SharedType.PerTestSession)]
public class C1514HostLoadNativeTests(C1514HostLoadFixture fixture)
{
    [Test, NativeLinuxOnly("CARD-1514: production Linux factory, host /proc topology and durable sample; CP-7 required on Linux.")]
    public async Task Linux_ProductionFactoryPreservesHostSamples()
    {
        OperatingSystem.IsLinux().ShouldBeTrue();
        await VerifyAsync(typeof(LinuxHostStatsProbe), LinuxOnlineCores());
    }

    [Test]
    public async Task Windows_ProductionFactoryPreservesHostSamples()
    {
        if (!OperatingSystem.IsWindows()) { Skip.Test("CARD-1514: Windows production factory/GetActiveProcessorCount witness; CP-8 required on Windows."); return; }
        await VerifyAsync(typeof(WindowsHostStatsProbe), checked((int)GetActiveProcessorCount(ushort.MaxValue)));
    }

    private async Task VerifyAsync(Type arm, int expectedCores)
    {
        const string historyKey = "SessionRunner:HostStats:HistoryPath";
        var absent = new Dictionary<string, string?>(); absent.ContainsKey(historyKey).ShouldBeFalse();
        var absentServices = new ServiceCollection(); absentServices.AddLogging();
        absentServices.AddHostStats(new ConfigurationBuilder().AddInMemoryCollection(absent).Build(), startSampler: false);
        using (var absentProvider = absentServices.BuildServiceProvider())
            absentProvider.GetRequiredService<IOptions<HostStatsSettings>>().Value.HistoryPath.ShouldBe(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "antiphon", "host-load"));
        var empty = new Dictionary<string, string?> { [historyKey] = "" }; empty[historyKey].ShouldBe("");
        var emptyServices = new ServiceCollection(); emptyServices.AddLogging();
        emptyServices.AddHostStats(new ConfigurationBuilder().AddInMemoryCollection(empty).Build(), startSampler: false);
        using (var emptyProvider = emptyServices.BuildServiceProvider())
            Should.Throw<InvalidOperationException>(() => emptyProvider.GetRequiredService<IOptions<HostStatsSettings>>().Value);
        var root = fixture.PrivatePath();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SessionRunner:HostStats:HistoryPath"] = root,
            ["SessionRunner:HostStats:PhysicalHostKey"] = "owned-host",
            ["SessionRunner:HostStats:RunnerId"] = "owned-runner",
            ["SessionRunner:HostStats:Volumes:0"] = Path.GetTempPath(),
        }).Build();
        config[historyKey].ShouldBe(root);
        var services = new ServiceCollection(); services.AddLogging(); services.AddHostStats(config, startSampler: false);
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHostStatsProbe>(); factory.ShouldBeOfType<SystemHostStatsProbe>();
        typeof(SystemHostStatsProbe).GetField("_arm", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(factory)!.GetType().ShouldBe(arm);
        var first = factory.Read().ShouldNotBeNull(); first.CpuPercent.ShouldBeNull(); first.Cores.ShouldBe(expectedCores); expectedCores.ShouldBeGreaterThan(0);
        await Task.Delay(1000); var sample = factory.Read().ShouldNotBeNull(); sample.CpuPercent.ShouldNotBeNull();
        double.IsFinite(sample.CpuPercent.Value).ShouldBeTrue(); sample.CpuPercent.Value.ShouldBeInRange(0d, 100d);
        sample.MemoryTotalBytes.ShouldNotBeNull(); sample.MemoryAvailableBytes.ShouldNotBeNull(); sample.MemoryTotalBytes.Value.ShouldBeGreaterThan(0);
        sample.Cores.ShouldBe(expectedCores);
        if (arm == typeof(LinuxHostStatsProbe)) { sample.Load1.ShouldNotBeNull(); sample.ProcessCount.ShouldNotBeNull(); }
        else { sample.Load1.ShouldBeNull(); sample.SwapTotalBytes.ShouldNotBeNull(); }
        var settings = provider.GetRequiredService<IOptions<HostStatsSettings>>().Value;
        var spool = new HostLoadSpool(root, settings, TimeProvider.System); var source = spool.NextSource();
        spool.Append(HostLoadProjection.Create(sample, source, settings, HostAdmissionSnapshot.Unknown("owned-host")));
        var stored = new HostLoadSpool(root, settings, TimeProvider.System).ReadHistory(null, 120).Samples.Single();
        stored.Source.ShouldBe(source); stored.CpuPercent.ShouldBe(sample.CpuPercent); stored.HostLogicalCores.ShouldBe(expectedCores);
        stored.BusyCoreEquivalent.ShouldBe(sample.CpuPercent * expectedCores / 100d);
        if (arm == typeof(WindowsHostStatsProbe)) stored.UnknownReasons["load1"].ShouldBe("unsupported");
        Console.WriteLine("C1514 native factory=" + factory.GetType().FullName + " arm=" + arm.FullName + " cores=" + expectedCores + " source=" + source);
    }
    internal static int LinuxOnlineCores() => File.ReadAllText("/sys/devices/system/cpu/online").Trim().Split(',')
        .Sum(part => { var bounds = part.Split('-'); return bounds.Length == 1 ? 1 : int.Parse(bounds[1]) - int.Parse(bounds[0]) + 1; });
    [DllImport("kernel32.dll")] internal static extern uint GetActiveProcessorCount(ushort group);
}
