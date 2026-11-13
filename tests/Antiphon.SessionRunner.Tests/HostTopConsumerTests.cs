using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>CARD-1884. Synthetic pid tables. A shared compiler under pid 1 is a top consumer.</summary>
[Category("Unit")]
public class HostTopConsumerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);

    [Test]
    [NativeLinuxOnly("CARD-1884 reads /proc. The Windows production-factory witness is Production_factory_observes_this_process_on_windows.")]
    public void Production_factory_selects_the_linux_proc_table()
    {
        OperatingSystem.IsLinux().ShouldBeTrue();
        var created = HostProcessTables.Create();
        created.ShouldBeOfType<LinuxHostProcessTable>();
        var live = created.Read(HostTopConsumerLimits.TopCount, Array.Empty<int>());
        live.Count.ShouldBeLessThanOrEqualTo(HostTopConsumerLimits.TopCount);

        using var provider = Services();
        provider.GetRequiredService<IHostProcessTable>().ShouldBeOfType<LinuxHostProcessTable>();
        provider.GetRequiredService<IHostStatsProbe>().ShouldBeOfType<SystemHostStatsProbe>();
    }

    [Test]
    public void Factory_selects_the_platform_reader()
    {
        HostProcessTables.Select(isWindows: false, isLinux: true).ShouldBeOfType<LinuxHostProcessTable>();
        HostProcessTables.Select(isWindows: true, isLinux: false).ShouldBeOfType<WindowsHostProcessTable>();
        HostProcessTables.Select(isWindows: false, isLinux: false).ShouldBeOfType<EmptyHostProcessTable>();
    }

    /// <summary>
    /// Runs on Windows only. An empty <see cref="WindowsHostProcessTable"/> fails this witness:
    /// the production factory must see this process, its start time, a nonzero working set, and a CPU
    /// counter that moves after a spin. Toolhelp and GetProcessTimes are not called on Linux.
    /// </summary>
    [Test]
    public void Production_factory_observes_this_process_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("CARD-1884 Windows production-factory witness runs on Windows only. This Linux host does not call toolhelp or GetProcessTimes.");
            return;
        }

        using var self = Process.GetCurrentProcess();
        var table = HostProcessTables.Create();
        table.ShouldBeOfType<WindowsHostProcessTable>();
        var first = table.Read(HostTopConsumerLimits.ScanCap, [self.Id]).Single(row => row.Pid == self.Id);
        first.StartTicks.ShouldBeGreaterThan(0);
        first.RssBytes.ShouldNotBeNull().ShouldBeGreaterThan(0);
        var spinUntil = Stopwatch.StartNew();
        while (spinUntil.ElapsedMilliseconds < 40)
        {
        }

        var second = table.Read(HostTopConsumerLimits.ScanCap, [self.Id]).Single(row => row.Pid == self.Id);
        second.StartTicks.ShouldBe(first.StartTicks);
        second.CpuTicks.ShouldBeGreaterThan(first.CpuTicks);
    }

    [Test]
    public async Task Shared_compiler_under_pid_1_is_ranked_compiler()
    {
        var root = NewRoot();
        try
        {
            var secret = Encoding.UTF8.GetBytes("/secret/TOKEN/VBCSCompiler\0-pipename:SUPERSECRET\0");
            WriteProc(root, 1, "VBCSCompiler", 0, 0, 100, 100, secret, "TASK_TOKEN=SUPERSECRET");
            var table = new LinuxHostProcessTable(root, 4096, 100);
            var time = new FakeTimeProvider(T0);
            var store = new HostStatsStore(new HostStatsSettings(), time);
            var sampler = Sampler(new ScriptedHostStatsProbe(), store, time, new HostStatsSettings(), table);
            await sampler.SampleOnceAsync();
            store.Latest().ShouldNotBeNull().TopConsumers.ShouldBeEmpty();

            WriteProc(root, 1, "VBCSCompiler", 2000, 650, 100, 100, secret, "TASK_TOKEN=SUPERSECRET");
            time.Advance(TimeSpan.FromSeconds(5));
            await sampler.SampleOnceAsync();
            var row = store.Latest().ShouldNotBeNull().TopConsumers.ShouldHaveSingleItem();
            row.Pid.ShouldBe(1);
            row.StartTicks.ShouldBe(100);
            row.Name.ShouldBe("VBCSCompiler");
            row.Role.ShouldBe(HostConsumerRules.Compiler);
            row.CpuCores.ShouldNotBeNull().ShouldBe(5.3, 0.001);
            row.RssBytes.ShouldBe(409600);
            store.Latest().ShouldNotBeNull().Processes.ShouldBeEmpty();

            var json = JsonSerializer.Serialize(store.Snapshot().Current, PhoneHomeFraming.Json);
            json.ShouldContain("\"role\":\"compiler\"");
            json.ShouldNotContain("SUPERSECRET");
            json.ShouldNotContain("TOKEN");
            json.ShouldNotContain("pipename");
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Test]
    public void Pid_reuse_does_not_inherit_the_previous_counter()
    {
        var (time, tracker, table) = Start(Obs(1, 10, 0, "VBCSCompiler", "compiler"));
        table.Rows[0] = Obs(1, 10, 100, "VBCSCompiler", "compiler");
        time.Advance(TimeSpan.FromSeconds(5));
        var steady = tracker.Sample(table, time).ShouldHaveSingleItem();
        steady.CpuCores.ShouldNotBeNull().ShouldBe(0.2, 0.001);

        table.Rows[0] = Obs(1, 99, 100_000, "VBCSCompiler", "compiler");
        time.Advance(TimeSpan.FromSeconds(5));
        tracker.Sample(table, time).ShouldBeEmpty();

        table.Rows[0] = Obs(1, 99, 100_050, "VBCSCompiler", "compiler");
        time.Advance(TimeSpan.FromSeconds(5));
        var reused = tracker.Sample(table, time).ShouldHaveSingleItem();
        reused.StartTicks.ShouldBe(99);
        reused.CpuCores.ShouldNotBeNull().ShouldBe(0.1, 0.001);
        reused.CpuCores.Value.ShouldBeLessThan(2);
    }

    [Test]
    public void Vanished_pid_is_omitted_and_the_sibling_remains()
    {
        var root = NewRoot();
        try
        {
            WriteProc(root, 1, "VBCSCompiler", 0, 0, 10, 1);
            Directory.CreateDirectory(Path.Combine(root, "2"));
            var rows = new LinuxHostProcessTable(root, 4096, 100).Read(8, []);
            rows.ShouldHaveSingleItem().Pid.ShouldBe(1);
            rows.Single().Role.ShouldBe(HostConsumerRules.Compiler);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Test]
    public void Top_count_keeps_eight_highest()
    {
        var first = Enumerable.Range(1, 9).Select(pid => Obs(pid, 1, 0, "pwsh", "other")).ToArray();
        var (time, tracker, table) = Start(first);
        table.Rows.Clear();
        table.Rows.AddRange(Enumerable.Range(1, 9).Select(pid => Obs(pid, 1, pid * 100, "pwsh", "other")));
        time.Advance(TimeSpan.FromSeconds(5));
        var ranked = tracker.Sample(table, time);
        ranked.Count.ShouldBe(HostTopConsumerLimits.TopCount);
        ranked[0].Pid.ShouldBe(9);
        ranked[0].CpuCores.ShouldNotBeNull().ShouldBe(1.8, 0.001);
        ranked.Any(row => row.Pid == 1).ShouldBeFalse();
    }

    [Test]
    public void Command_line_and_environment_are_not_published()
    {
        var root = NewRoot();
        try
        {
            var cmdline = Encoding.UTF8.GetBytes("/secret/TOKEN/Antiphon.SessionRunner\0--task=SUPERSECRET\0");
            WriteProc(root, 4, "Antiphon.Sessio", 0, 0, 7, 2, cmdline, "TASK_TOKEN=SUPERSECRET");
            var row = new LinuxHostProcessTable(root, 4096, 100).Read(8, []).ShouldHaveSingleItem();
            row.Name.ShouldBe("Antiphon.SessionRunner");
            row.Role.ShouldBe(HostConsumerRules.AntiphonRunner);
            var json = JsonSerializer.Serialize(row, PhoneHomeFraming.Json);
            json.ShouldNotContain("SUPERSECRET");
            json.ShouldNotContain("TOKEN");
            json.ShouldNotContain("--task");
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Test]
    public void Scan_keeps_a_retained_low_pid_inside_the_cap()
    {
        var root = NewRoot();
        try
        {
            for (var pid = 1; pid <= 520; pid++)
                WriteProc(root, pid, "pwsh", 0, 0, 1, 1);
            var rows = new LinuxHostProcessTable(root, 4096, 100).Read(HostTopConsumerLimits.ScanCap, [1]);
            rows.Count.ShouldBe(HostTopConsumerLimits.ScanCap);
            rows.Any(row => row.Pid == 1).ShouldBeTrue();
            rows.Any(row => row.Pid == 2).ShouldBeFalse();
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Test]
    public void Short_interval_does_not_rescan_or_move_the_baseline()
    {
        var table = new ScriptedTable();
        table.Rows.Add(Obs(1, 1, 0, "VBCSCompiler", "compiler"));
        var time = new FakeTimeProvider(T0);
        var tracker = new HostTopConsumerTracker();
        tracker.Sample(table, time).ShouldBeEmpty();
        table.Reads.ShouldBe(1);

        table.Rows[0] = Obs(1, 1, 50_000, "VBCSCompiler", "compiler");
        time.Advance(TimeSpan.FromSeconds(1));
        tracker.Sample(table, time).ShouldBeEmpty();
        table.Reads.ShouldBe(1);

        time.Advance(TimeSpan.FromSeconds(4));
        var row = tracker.Sample(table, time).ShouldHaveSingleItem();
        table.Reads.ShouldBe(2);
        row.CpuCores.ShouldNotBeNull().ShouldBe(100, 0.001);
    }

    [Test]
    public void Scan_fault_returns_the_previous_ranking()
    {
        var (time, tracker, table) = Start(Obs(1, 1, 0, "csc", "compiler"));
        table.Rows[0] = Obs(1, 1, 200, "csc", "compiler");
        time.Advance(TimeSpan.FromSeconds(5));
        tracker.Sample(table, time).ShouldHaveSingleItem().CpuCores.ShouldNotBeNull().ShouldBe(0.4, 0.001);

        table.Fault = true;
        time.Advance(TimeSpan.FromSeconds(5));
        var kept = tracker.Sample(table, time).ShouldHaveSingleItem();
        kept.CpuCores.ShouldNotBeNull().ShouldBe(0.4, 0.001);
        table.Reads.ShouldBe(3);
    }

    [Test]
    public async Task Missing_table_publishes_no_top_consumers()
    {
        var time = new FakeTimeProvider(T0);
        var store = new HostStatsStore(new HostStatsSettings(), time);
        var sampler = Sampler(new ScriptedHostStatsProbe(), store, time, new HostStatsSettings(), null);
        await sampler.SampleOnceAsync();
        time.Advance(TimeSpan.FromSeconds(5));
        await sampler.SampleOnceAsync();
        store.Latest().ShouldNotBeNull().TopConsumers.ShouldBeEmpty();
    }

    [Test]
    public async Task Process_sampling_off_does_not_read_the_table()
    {
        var table = new ScriptedTable();
        table.Rows.Add(Obs(1, 1, 500, "VBCSCompiler", "compiler"));
        var time = new FakeTimeProvider(T0);
        var store = new HostStatsStore(new HostStatsSettings(), time);
        var sampler = Sampler(new ScriptedHostStatsProbe(), store, time, new HostStatsSettings { ProcessSampling = false }, table);
        await sampler.SampleOnceAsync();
        time.Advance(TimeSpan.FromSeconds(5));
        await sampler.SampleOnceAsync();
        table.Reads.ShouldBe(0);
        store.Latest().ShouldNotBeNull().TopConsumers.ShouldBeEmpty();
    }

    [Test]
    public void Unsafe_name_is_dropped()
    {
        var (time, tracker, table) = Start(
            Obs(1, 1, 0, "secret/TOKEN", "compiler"),
            Obs(2, 1, 0, "pwsh", "other"));
        table.Rows[0] = Obs(1, 1, 50_000, "secret/TOKEN", "compiler");
        table.Rows[1] = Obs(2, 1, 100, "pwsh", "other");
        time.Advance(TimeSpan.FromSeconds(5));
        var row = tracker.Sample(table, time).ShouldHaveSingleItem();
        row.Name.ShouldBe("pwsh");
        row.Role.ShouldBe(HostConsumerRules.Other);
    }

    [Test]
    public void Roles_follow_the_allow_list()
    {
        Classify("VBCSCompiler", []).ShouldBe(("VBCSCompiler", HostConsumerRules.Compiler));
        Classify("csc.exe", []).ShouldBe(("csc", HostConsumerRules.Compiler));
        Classify("vbc", []).ShouldBe(("vbc", HostConsumerRules.Compiler));
        Classify("MSBuild.exe", []).ShouldBe(("MSBuild", HostConsumerRules.Build));
        Classify("testhost", []).ShouldBe(("testhost", HostConsumerRules.Test));
        Classify("vstest.console.exe", []).ShouldBe(("vstest.console", HostConsumerRules.Test));
        Classify("Antiphon.Tests.dll", []).ShouldBe(("Antiphon.Tests", HostConsumerRules.Test));
        Classify("Antiphon.SessionRunner", []).ShouldBe(("Antiphon.SessionRunner", HostConsumerRules.AntiphonRunner));
        Classify("Antiphon.PtyHost", []).ShouldBe(("Antiphon.PtyHost", HostConsumerRules.PtyHost));
        Classify("dockerd", []).ShouldBe(("dockerd", HostConsumerRules.Docker));
        Classify("containerd-shim", []).ShouldBe(("containerd-shim", HostConsumerRules.Docker));
        Classify("grok", []).ShouldBe(("grok", HostConsumerRules.AgentCli));
        Classify("claude", []).ShouldBe(("claude", HostConsumerRules.AgentCli));
        Classify("codex", []).ShouldBe(("codex", HostConsumerRules.AgentCli));
        Classify("dotnet", ["dotnet", "test"]).ShouldBe(("dotnet", HostConsumerRules.Test));
        Classify("dotnet", ["dotnet", "build"]).ShouldBe(("dotnet", HostConsumerRules.Build));
        Classify("pwsh", []).ShouldBe(("pwsh", HostConsumerRules.Other));
        Classify("Antiphon.Sessio", ["Antiphon.SessionRunner"]).ShouldBe(("Antiphon.SessionRunner", HostConsumerRules.AntiphonRunner));
    }

    [Test]
    public void Old_history_without_top_consumers_deserializes_empty()
    {
        const string json = "{\"schemaVersion\":1,\"source\":{\"runnerId\":\"runner-a\",\"runnerStoreId\":\"11111111-1111-1111-1111-111111111111\",\"bootId\":\"22222222-2222-2222-2222-222222222222\",\"sequence\":1},\"sampledAt\":\"2026-11-13T00:00:00+00:00\",\"intervalSeconds\":5,\"processes\":[{\"name\":\"runner\",\"sessionId\":null,\"cpuPercent\":1,\"workingSetBytes\":2}]}";
        var sample = JsonSerializer.Deserialize<HostLoadSample>(json, PhoneHomeFraming.Json);
        sample.ShouldNotBeNull();
        sample.SchemaVersion.ShouldBe(1);
        sample.TopConsumers.ShouldBeEmpty();
        sample.Processes.Single().Name.ShouldBe("runner");
    }

    [Test]
    public void Projection_keeps_eight_publishable_rows()
    {
        var top = new List<HostTopConsumerSample>
        {
            new(1, 10, "VBCSCompiler", HostConsumerRules.Compiler, 5.3, 409600),
            new(2, 10, "secret/TOKEN", HostConsumerRules.Compiler, 9, 1),
            new(3, 10, "pipename=SUPERSECRET", HostConsumerRules.Other, 8, 1),
        };
        for (var i = 0; i < 8; i++)
            top.Add(new HostTopConsumerSample(100 + i, 10, "pwsh", HostConsumerRules.Other, 0.1, 1024));
        var read = new HostSample(T0, 10, 8, null, null, null, null, null, null, null, [], null, [])
        {
            TopConsumers = top,
        };
        var source = new HostLoadSource("runner-a", Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"), 1);
        var sample = HostLoadProjection.Create(read, source, new HostStatsSettings(), HostAdmissionSnapshot.Unknown());
        sample.SchemaVersion.ShouldBe(1);
        sample.TopConsumers.Count.ShouldBe(HostConsumerRules.MaxCount);
        sample.TopConsumers[0].Name.ShouldBe("VBCSCompiler");
        sample.TopConsumers[0].Role.ShouldBe(HostConsumerRules.Compiler);
        sample.TopConsumers.Any(row => row.Name.Contains('/') || row.Name.Contains('=')).ShouldBeFalse();
        var json = JsonSerializer.Serialize(sample, PhoneHomeFraming.Json);
        json.ShouldNotContain("SUPERSECRET");
        json.ShouldNotContain("TOKEN");
    }

    [Test]
    [NativeLinuxOnly("CARD-1884 live /proc scan. Windows owes the production-factory witness and the sampler overhead budget.")]
    public void Live_proc_scan_stays_inside_the_tick_budget()
    {
        OperatingSystem.IsLinux().ShouldBeTrue();
        var table = new LinuxHostProcessTable("/proc");
        table.Read(HostTopConsumerLimits.TopCount, Array.Empty<int>());
        var watch = Stopwatch.StartNew();
        var rows = table.Read(HostTopConsumerLimits.ScanCap, Array.Empty<int>());
        watch.Stop();
        Console.WriteLine("CARD-1884 proc scan rows=" + rows.Count.ToString(CultureInfo.InvariantCulture) + " elapsedMs=" + watch.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
        rows.Count.ShouldBeGreaterThan(0);
        rows.Count.ShouldBeLessThanOrEqualTo(HostTopConsumerLimits.ScanCap);
        watch.Elapsed.TotalMilliseconds.ShouldBeLessThan(25);
    }

    [Test]
    public void Source_does_not_open_environ_or_snapshot_every_process()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src/Antiphon.SessionRunner/HostProcessTable.cs"));
        source.ShouldNotContain("NtQuerySystemInformation");
        source.ShouldNotContain("GetProcesses");
        source.ShouldNotContain("/environ");
        source.ShouldNotContain("\"environ\"");
    }

    private static (string Name, string Role) Classify(string comm, string[] args)
        => HostConsumerClassifier.Classify(comm, args);

    private static (FakeTimeProvider Time, HostTopConsumerTracker Tracker, ScriptedTable Table) Start(params HostProcessObservation[] rows)
    {
        var table = new ScriptedTable();
        table.Rows.AddRange(rows);
        var time = new FakeTimeProvider(T0);
        var tracker = new HostTopConsumerTracker();
        tracker.Sample(table, time).ShouldBeEmpty();
        return (time, tracker, table);
    }

    private static HostProcessObservation Obs(int pid, long start, long cpu, string name, string role)
        => new(pid, start, cpu, 100, 4096, name, role);

    private static HostStatsSamplerService Sampler(
        ScriptedHostStatsProbe probe,
        HostStatsStore store,
        TimeProvider time,
        HostStatsSettings settings,
        IHostProcessTable? table)
        => new(
            probe,
            store,
            new NoCpu(),
            static () => Array.Empty<HostStatsProcessTarget>(),
            static _ => null,
            time,
            Options.Create(settings),
            NullLogger<HostStatsSamplerService>.Instance,
            null,
            table);

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHostStats(new ConfigurationBuilder().Build(), startSampler: false);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Fields after ") " are state, ten counters, utime, stime, six more, starttime, vsize, rss.
    /// A trailing field keeps the newline off rss. 2650 ticks at 100 Hz over 5 s is 5.3 cores.
    /// </summary>
    private static void WriteProc(string root, int pid, string comm, long utime, long stime, long start, long rssPages, byte[]? cmdline = null, string? environ = null)
    {
        var dir = Path.Combine(root, pid.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "stat"),
            "1 (" + comm + ") R 0 0 0 0 0 0 0 0 0 0 " + utime.ToString(CultureInfo.InvariantCulture) + " " + stime.ToString(CultureInfo.InvariantCulture) + " 0 0 20 0 1 0 " + start.ToString(CultureInfo.InvariantCulture) + " 0 " + rssPages.ToString(CultureInfo.InvariantCulture) + " 0\n");
        if (cmdline is not null)
            File.WriteAllBytes(Path.Combine(dir, "cmdline"), cmdline);
        if (environ is not null)
            File.WriteAllText(Path.Combine(dir, "environ"), environ);
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "card-1884-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        dir.ShouldNotBeNull();
        return dir.FullName;
    }

    private sealed class NoCpu : IProcessCpuProbe
    {
        public TimeSpan? TryGetTotalCpuTime(int pid, DateTime startedAt) => null;
    }

    private sealed class ScriptedTable : IHostProcessTable
    {
        public List<HostProcessObservation> Rows { get; } = [];
        public int Reads { get; private set; }
        public bool Fault { get; set; }

        public IReadOnlyList<HostProcessObservation> Read(int maxProcesses, IReadOnlyCollection<int> retainPids)
        {
            Reads++;
            if (Fault)
                throw new IOException("scan failed");
            return Rows.ToArray();
        }
    }
}
