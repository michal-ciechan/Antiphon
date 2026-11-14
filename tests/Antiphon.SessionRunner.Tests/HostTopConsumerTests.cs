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
            sampler.ScanTopConsumers();
            await sampler.SampleOnceAsync();
            store.Latest().ShouldNotBeNull().TopConsumers.ShouldBeEmpty();

            WriteProc(root, 1, "VBCSCompiler", 2000, 650, 100, 100, secret, "TASK_TOKEN=SUPERSECRET");
            time.Advance(TimeSpan.FromSeconds(5));
            sampler.ScanTopConsumers();
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

    /// <summary>
    /// CARD-1884 review. The tick only reads the cached ranking. A scan that blocks inside Read
    /// must not keep SampleOnceAsync from finishing.
    /// </summary>
    [Test, Timeout(5_000)]
    public async Task Blocking_scan_does_not_stretch_the_tick()
    {
        using var release = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        var table = new ScriptedTable { Gate = release, Entered = entered };
        table.Rows.Add(Obs(1, 1, 500, "VBCSCompiler", "compiler"));
        var time = new FakeTimeProvider(T0);
        var store = new HostStatsStore(new HostStatsSettings(), time);
        var sampler = Sampler(new ScriptedHostStatsProbe(), store, time, new HostStatsSettings(), table);
        var tick = Task.Run(() => sampler.SampleOnceAsync());
        var finished = await Task.WhenAny(tick, Task.Delay(150));
        try
        {
            finished.ShouldBe(tick, "the sampler tick must finish without waiting for the top-consumer scan");
            entered.IsSet.ShouldBeFalse();
            store.Latest().ShouldNotBeNull();
        }
        finally
        {
            release.Set();
            await tick.WaitAsync(TimeSpan.FromSeconds(2));
        }
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
    public void Eight_rows_add_a_bounded_wire_delta()
    {
        var empty = Wire(Array.Empty<HostLoadTopConsumer>());
        var rows = new HostLoadTopConsumer[8];
        for (var i = 0; i < 8; i++)
            rows[i] = new HostLoadTopConsumer(1000 + i, 100, "pwsh", "other", 1.25, 409600);
        var full = Wire(rows);
        var emptyBytes = Encoding.UTF8.GetByteCount(empty);
        var fullBytes = Encoding.UTF8.GetByteCount(full);
        var delta = fullBytes - emptyBytes;
        Console.WriteLine("CARD-1884 wire emptyBytes=" + emptyBytes.ToString(CultureInfo.InvariantCulture)
            + " fullBytes=" + fullBytes.ToString(CultureInfo.InvariantCulture)
            + " delta=" + delta.ToString(CultureInfo.InvariantCulture));
        delta.ShouldBeGreaterThan(0);
        delta.ShouldBeLessThan(2048);
        full.ShouldContain("pwsh");
    }

    private static string Wire(IReadOnlyList<HostLoadTopConsumer> top)
    {
        var sample = new HostLoadSample
        {
            Source = new HostLoadSource("runner-a", Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"), 1),
            SampledAt = T0,
            IntervalSeconds = 5,
            CpuPercent = 10,
            TopConsumers = top,
        };
        return JsonSerializer.Serialize(sample, PhoneHomeFraming.Json);
    }

    [Test]
    [NativeLinuxOnly("CARD-1884 live /proc scan. The tick no longer waits for it. Windows owes toolhelp and the overhead budget.")]
    public void Live_proc_scan_reads_proc_off_the_tick()
    {
        OperatingSystem.IsLinux().ShouldBeTrue();
        var table = new LinuxHostProcessTable("/proc");
        var rows = table.Read(HostTopConsumerLimits.ScanCap, Array.Empty<int>());
        Console.WriteLine("CARD-1884 proc scan rows=" + rows.Count.ToString(CultureInfo.InvariantCulture)
            + " truncated=" + table.LastReadTruncated.ToString());
        rows.Count.ShouldBeGreaterThan(0);
        rows.Count.ShouldBeLessThanOrEqualTo(HostTopConsumerLimits.ScanCap);
    }

    /// <summary>
    /// CARD-1884 round 3. The hosted scan owns the read. Ticks keep serving the cached ranking,
    /// and a second scan does not start while the first read is held.
    /// </summary>
    [Test, Timeout(8_000)]
    public async Task Held_scan_keeps_ticks_moving_and_does_not_overlap()
    {
        using var release = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        using var stopPump = new ManualResetEventSlim(false);
        var table = new ScriptedTable { Gate = release, Entered = entered, HoldFromRead = 3 };
        table.Rows.Add(Obs(1, 1, 0, "csc", "compiler"));
        var time = new FakeTimeProvider(T0);
        var store = new HostStatsStore(new HostStatsSettings(), time);
        var sampler = Sampler(new ScriptedHostStatsProbe(), store, time, new HostStatsSettings(), table);
        // FakeTimeProvider runs timer callbacks on the thread that calls Advance. A held Read
        // must not pin that thread, so the clock pump stays off the test thread and starts
        // only after the first read, once the row has a CPU delta to publish.
        Task pump = Task.CompletedTask;
        try
        {
            await sampler.StartAsync(CancellationToken.None);
            var first = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (table.Completed < 1 && DateTime.UtcNow < first)
                await Task.Delay(10);
            table.Completed.ShouldBeGreaterThanOrEqualTo(1);
            table.Rows[0] = Obs(1, 1, 50_000, "csc", "compiler");
            pump = Task.Run(async () =>
            {
                while (!entered.IsSet && !stopPump.IsSet)
                {
                    time.Advance(TimeSpan.FromSeconds(5));
                    await Task.Delay(10);
                }
            });
            entered.Wait(TimeSpan.FromSeconds(3)).ShouldBeTrue();
            table.Reads.ShouldBeGreaterThanOrEqualTo(2);
            var tick = Task.Run(() => sampler.SampleOnceAsync());
            var finished = await Task.WhenAny(tick, Task.Delay(150));
            finished.ShouldBe(tick, "a held scan must not stretch the sampler tick");
            await tick;
            time.Advance(TimeSpan.FromSeconds(11));
            var aged = Task.Run(() => sampler.SampleOnceAsync());
            (await Task.WhenAny(aged, Task.Delay(150))).ShouldBe(aged);
            await aged;
            var sample = store.Latest().ShouldNotBeNull();
            sample.TopConsumersStale.ShouldBeTrue();
            sample.TopConsumersAgeSeconds.ShouldNotBeNull().ShouldBeGreaterThan(10);
            sample.TopConsumers.ShouldHaveSingleItem().Name.ShouldBe("csc");
            await Task.Delay(50);
            table.Reads.ShouldBe(3, "no further scan starts while one read is held");
        }
        finally
        {
            release.Set();
            stopPump.Set();
            await pump;
            await sampler.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>CARD-1884 round 3. Stop joins the scan with a bounded wait and drops a late read.</summary>
    [Test, Timeout(8_000)]
    public async Task Stop_while_scan_is_held_discards_the_late_result()
    {
        using var release = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        var table = new ScriptedTable { Gate = release, Entered = entered };
        table.Rows.Add(Obs(99, 5, 50_000, "VBCSCompiler", "compiler"));
        var time = new FakeTimeProvider(T0);
        var store = new HostStatsStore(new HostStatsSettings(), time);
        var sampler = Sampler(new ScriptedHostStatsProbe(), store, time, new HostStatsSettings(), table);
        try
        {
            await sampler.StartAsync(CancellationToken.None);
            entered.Wait(TimeSpan.FromSeconds(2)).ShouldBeTrue();
            var started = Stopwatch.StartNew();
            await sampler.StopAsync(CancellationToken.None);
            started.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3), "stop must return within the bounded scan join");
            table.Rows[0] = Obs(99, 5, 9_000_000, "VBCSCompiler", "compiler");
            release.Set();
            await Task.Delay(200);
            await sampler.SampleOnceAsync();
            var sample = store.Latest().ShouldNotBeNull();
            sample.TopConsumers.ShouldBeEmpty();
            sample.TopConsumersAgeSeconds.ShouldBeNull();
        }
        finally
        {
            release.Set();
        }
    }

    /// <summary>
    /// CARD-1884 round 4. The epoch check and the publish are one operation. Holding Observe
    /// after the check lets StopAsync return; releasing the hold must not publish.
    /// </summary>
    [Test, Timeout(15_000)]
    public async Task Stop_after_the_epoch_check_discards_the_held_observe()
    {
        using var release = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        var generation = 0;
        var table = new ScriptedTable();
        table.Rows.Add(Obs(99, 5, 0, "VBCSCompiler", "compiler"));
        var time = new FakeTimeProvider(T0);
        var store = new HostStatsStore(new HostStatsSettings(), time);
        var sampler = Sampler(new ScriptedHostStatsProbe(), store, time, new HostStatsSettings(), table);
        sampler.BeforeTopConsumerPublish = () =>
        {
            if (Interlocked.Increment(ref generation) < 2)
                return;
            entered.Set();
            release.Wait();
        };
        var pump = Task.CompletedTask;
        using var stopPump = new ManualResetEventSlim(false);
        try
        {
            await sampler.StartAsync(CancellationToken.None);
            var first = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (table.Completed < 1 && DateTime.UtcNow < first)
                await Task.Delay(10);
            table.Completed.ShouldBeGreaterThanOrEqualTo(1);
            table.Rows[0] = Obs(99, 5, 9_000_000, "VBCSCompiler", "compiler");
            pump = Task.Run(async () =>
            {
                while (!entered.IsSet && !stopPump.IsSet)
                {
                    time.Advance(TimeSpan.FromSeconds(5));
                    await Task.Delay(10);
                }
            });
            entered.Wait(TimeSpan.FromSeconds(3)).ShouldBeTrue();
            var started = Stopwatch.StartNew();
            await sampler.StopAsync(CancellationToken.None);
            started.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3), "stop returns while Observe is held");
            release.Set();
            await Task.Delay(200);
            await sampler.SampleOnceAsync();
            var sample = store.Latest().ShouldNotBeNull();
            sample.TopConsumers.ShouldBeEmpty("a publish from the stopped epoch is discarded");
        }
        finally
        {
            release.Set();
            stopPump.Set();
            await pump;
        }
    }

    /// <summary>CARD-1884 round 3. Dispose is idempotent and a late read cannot publish after it.</summary>
    [Test, Timeout(8_000)]
    public async Task Dispose_twice_while_scan_is_held_drops_the_late_result()
    {
        using var release = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        var table = new ScriptedTable { Gate = release, Entered = entered };
        table.Rows.Add(Obs(99, 5, 50_000, "VBCSCompiler", "compiler"));
        var time = new FakeTimeProvider(T0);
        var store = new HostStatsStore(new HostStatsSettings(), time);
        var sampler = Sampler(new ScriptedHostStatsProbe(), store, time, new HostStatsSettings(), table);
        try
        {
            await sampler.StartAsync(CancellationToken.None);
            entered.Wait(TimeSpan.FromSeconds(2)).ShouldBeTrue();
            sampler.Dispose();
            Should.NotThrow(() => sampler.Dispose());
            release.Set();
            await Task.Delay(200);
            await sampler.SampleOnceAsync();
            store.Latest().ShouldNotBeNull().TopConsumersAgeSeconds.ShouldBeNull();
        }
        finally
        {
            release.Set();
            await sampler.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>CARD-1884 round 3. A scan fault is logged once and does not fault the hosted loop.</summary>
    [Test, Timeout(8_000)]
    public async Task Scan_exception_is_logged_once_and_contained()
    {
        var table = new ScriptedTable { Fault = true };
        var time = new FakeTimeProvider(T0);
        var store = new HostStatsStore(new HostStatsSettings(), time);
        var logger = new ScanLogger();
        var sampler = Sampler(new ScriptedHostStatsProbe(), store, time, new HostStatsSettings(), table, logger);
        try
        {
            await sampler.StartAsync(CancellationToken.None);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            while (table.Reads < 3 && DateTime.UtcNow < deadline)
            {
                time.Advance(TimeSpan.FromSeconds(5));
                await Task.Delay(20);
            }

            table.Reads.ShouldBeGreaterThanOrEqualTo(3);
            logger.Warnings.ShouldBe(1);
            store.Latest().ShouldNotBeNull();
        }
        finally
        {
            await sampler.StopAsync(CancellationToken.None);
        }
    }

    [Test, Timeout(30_000)]
    public void Missing_constant_and_backwards_clocks_fail_closed()
    {
        var root = WritePidDirectories(5000, statsFrom: 4980);
        try
        {
            var backwards = 100d;
            Func<double?>[] clocks =
            [
                () => null,
                () => 5d,
                () => backwards--,
            ];
            foreach (var clock in clocks)
            {
                var table = new LinuxHostProcessTable(root, 4096, 100, clock, TimeSpan.FromMilliseconds(10));
                var rows = table.Read(HostTopConsumerLimits.ScanCap, Array.Empty<int>());
                table.LastReadTruncated.ShouldBeTrue();
                rows.Count.ShouldBeLessThanOrEqualTo(32);
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    /// <summary>
    /// CARD-1884 round 4. 0,1,1,1,... and 0,5,4,4,... are stuck or moving backwards against the
    /// previous reading. Comparing them only with the origin reads the whole cap.
    /// </summary>
    [Test, Timeout(30_000)]
    public void Stalled_and_regressing_clocks_fail_closed()
    {
        var root = NewRoot();
        try
        {
            for (var pid = 1; pid <= 520; pid++)
                WriteProc(root, pid, "pwsh", 10, 10, 1, 1);
            AssertClockFailsClosed(root, StallThenFlat(), "0,1,1,1");
            AssertClockFailsClosed(root, AdvanceThenRegress(), "0,5,4");
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static void AssertClockFailsClosed(string root, Func<double?> clock, string sequence)
    {
        var table = new LinuxHostProcessTable(root, 4096, 100, clock, TimeSpan.FromMilliseconds(10));
        var rows = table.Read(HostTopConsumerLimits.ScanCap, Array.Empty<int>());
        table.LastReadTruncated.ShouldBeTrue(sequence);
        rows.Count.ShouldBeLessThanOrEqualTo(32, sequence);
    }

    /// <summary>Constructor consumes 0. Every later reading is 1.</summary>
    private static Func<double?> StallThenFlat()
    {
        var phase = 0;
        return () => Interlocked.Increment(ref phase) == 1 ? 0d : 1d;
    }

    /// <summary>Constructor consumes 0, the next reading is 5, and every later reading is 4.</summary>
    private static Func<double?> AdvanceThenRegress()
    {
        var phase = 0;
        return () =>
        {
            var n = Interlocked.Increment(ref phase);
            if (n == 1) return 0d;
            return n == 2 ? 5d : 4d;
        };
    }

    [Test, Timeout(30_000)]
    public void Enumeration_cost_exhausts_the_budget_before_any_process_read()
    {
        var root = WritePidDirectories(5000, statsFrom: 4980);
        try
        {
            double cpu = 0;
            var table = new LinuxHostProcessTable(root, 4096, 100, () =>
            {
                cpu += 1;
                return cpu;
            }, TimeSpan.FromMilliseconds(10));
            var rows = table.Read(HostTopConsumerLimits.ScanCap, Array.Empty<int>());
            table.LastReadTruncated.ShouldBeTrue();
            rows.Count.ShouldBe(0);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Test]
    public void Scan_stops_when_the_cpu_budget_is_spent()
    {
        var root = NewRoot();
        try
        {
            for (var pid = 1; pid <= 4; pid++)
                WriteProc(root, pid, "pwsh", 10, 10, 1, 1);
            double cpu = 0;
            // The budget now includes directory enumeration. 1.8 ms per sample leaves four
            // directories inside the 10 ms budget and stops on a later process read.
            var table = new LinuxHostProcessTable(root, 4096, 100, () =>
            {
                cpu += 1.8;
                return cpu;
            }, TimeSpan.FromMilliseconds(10));
            var rows = table.Read(HostTopConsumerLimits.ScanCap, Array.Empty<int>());
            table.LastReadTruncated.ShouldBeTrue();
            rows.Count.ShouldBeGreaterThan(0);
            rows.Count.ShouldBeLessThan(4);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Test]
    public void Cached_scan_is_stale_after_twice_the_cadence()
    {
        var (time, tracker, table) = Start(Obs(1, 1, 0, "csc", "compiler"));
        var unseen = tracker.View(time, TimeSpan.FromSeconds(5));
        unseen.AgeSeconds.ShouldNotBeNull();
        unseen.Stale.ShouldBeFalse();
        table.Rows[0] = Obs(1, 1, 200, "csc", "compiler");
        time.Advance(TimeSpan.FromSeconds(5));
        tracker.Sample(table, time).ShouldHaveSingleItem();
        var fresh = tracker.View(time, TimeSpan.FromSeconds(5));
        fresh.Stale.ShouldBeFalse();
        fresh.AgeSeconds.ShouldNotBeNull().ShouldBeLessThan(1);
        time.Advance(TimeSpan.FromSeconds(11));
        var old = tracker.View(time, TimeSpan.FromSeconds(5));
        old.Stale.ShouldBeTrue();
        old.AgeSeconds.ShouldNotBeNull().ShouldBeGreaterThan(10);
        old.Rows.ShouldHaveSingleItem().Name.ShouldBe("csc");
    }

    [Test]
    public void Source_does_not_open_environ_or_snapshot_every_process()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src/Antiphon.SessionRunner/HostProcessTable.cs"));
        source.ShouldNotContain("NtQuerySystemInformation");
        source.ShouldNotContain("GetProcesses");
        source.ShouldNotContain("/environ");
        source.ShouldNotContain("\"environ\"");
        source.ShouldNotContain("ReadAllText");
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
        IHostProcessTable? table,
        ILogger<HostStatsSamplerService>? logger = null)
        => new(
            probe,
            store,
            new NoCpu(),
            static () => Array.Empty<HostStatsProcessTarget>(),
            static _ => null,
            time,
            Options.Create(settings),
            logger ?? NullLogger<HostStatsSamplerService>.Instance,
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

    private static string WritePidDirectories(int count, int statsFrom)
    {
        var root = NewRoot();
        for (var pid = 1; pid <= count; pid++)
        {
            var dir = Path.Combine(root, pid.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(dir);
            if (pid >= statsFrom)
                WriteProc(root, pid, "pwsh", 10, 10, 1, 1);
        }

        return root;
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

    private sealed class ScanLogger : ILogger<HostStatsSamplerService>
    {
        public int Warnings { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning) Warnings++;
        }
    }

    private sealed class NoCpu : IProcessCpuProbe
    {
        public TimeSpan? TryGetTotalCpuTime(int pid, DateTime startedAt) => null;
    }

    private sealed class ScriptedTable : IHostProcessTable
    {
        public List<HostProcessObservation> Rows { get; } = [];
        public int Reads { get; private set; }
        public int Completed => Volatile.Read(ref _completed);
        private int _completed;
        public bool Fault { get; set; }
        public bool LastReadTruncated => false;
        public ManualResetEventSlim? Gate { get; init; }
        public ManualResetEventSlim? Entered { get; init; }
        public int HoldFromRead { get; init; } = 1;

        public IReadOnlyList<HostProcessObservation> Read(int maxProcesses, IReadOnlyCollection<int> retainPids, CancellationToken cancellationToken = default)
        {
            Reads++;
            if (Reads >= HoldFromRead)
            {
                Entered?.Set();
                Gate?.Wait();
            }

            if (Fault)
                throw new IOException("scan failed");
            var rows = Rows.ToArray();
            Interlocked.Increment(ref _completed);
            return rows;
        }
    }
}
