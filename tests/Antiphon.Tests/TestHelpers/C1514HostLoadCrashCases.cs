using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Scripts;
using Antiphon.Tests.Application;
using Microsoft.EntityFrameworkCore;
using Shouldly;
namespace Antiphon.Tests.TestHelpers;
internal static class C1514HostLoadCrashCases
{
    public static async Task RunAsync(C1514HostLoadFixture fixture, string cut, CancellationToken ct)
    {
        using var scenarioStop = CancellationTokenSource.CreateLinkedTokenSource(ct, C1514HostLoadScenario.Current?.Token ?? CancellationToken.None);
        ct = scenarioStop.Token;
        var probe = Path.Combine(AppContext.BaseDirectory, "host-load-probe", "Antiphon.HostLoad.Probe.dll"); File.Exists(probe).ShouldBeTrue("The probe is staged by the checkpoint build, never built inside a test.");
        {
            using var phase = TestTiming.BeginPhase(TestPhaseKind.ProcessWait, "host-load crash " + cut);
            Console.WriteLine("c1514 crash cut=" + cut);
            await using var world = await fixture.WorldAsync(phoneHome: true);
            HostLoadSample sample;
            if (cut.StartsWith("append-", StringComparison.Ordinal)) sample = new() { Source = new("grok-linux", world.Spool.RunnerStoreId, world.Spool.BootId, 101), SampledAt = C1514HostLoadWorld.T0,
                PhysicalHostKey = "owned-host", SourceEpoch = world.Runner.SourceEpoch, BindingRevision = 1, CpuPercent = 40, HostLogicalCores = 24, BusyCoreEquivalent = 9.6 };
            else { await world.SeedPrefixAsync(); sample = await world.ProduceAsync(C1514HostLoadWorld.T0); }
            Guid? taskId = null;
            if (cut.StartsWith("derive-", StringComparison.Ordinal))
            {
                var task = await world.StartTaskAsync(C1514HostLoadWorld.T0.AddSeconds(-1)); taskId = task.Id;
                await world.FinishTaskAsync(task.Id, C1514HostLoadWorld.T0.AddSeconds(1));
                world.Clock.Utc = C1514HostLoadWorld.T0.AddSeconds(2);
            }
            var nonce = Guid.NewGuid().ToString("N"); var ready = Path.Combine(world.Root, "ready"); var initialized = Path.Combine(world.Root, "initialized"); var config = Path.Combine(world.Root, "probe-config.json");
            File.WriteAllText(config, JsonSerializer.Serialize(new { world.Root, SourceUrl = world.ProxyUrl, ConnectionString = world.Schema.ConnectionString, Now = world.Clock.GetUtcNow(), Cut = cut, Nonce = nonce, ReadyPath = ready, InitializedPath = initialized, Sample = sample }, PhoneHomeFraming.Json));
            // Keep the ScriptHarness supervisor/group custody and bounded drain.
            // Linux can exec the staged .NET worker without another interpreter.
            string? executable = null;
            if (OperatingSystem.IsLinux())
            {
                executable = Path.Combine(world.Root, "launch-probe.sh");
                static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
                File.WriteAllText(executable, "#!/bin/sh\nexec dotnet " + Quote(probe) + " " + Quote(config) + "\n");
                File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            var script = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "test-host-load.ps1"); using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var owned = world.Own(ScriptHarnessProcess.RunAsync("test-host-load.ps1", "c1514-crash", "Probe", script,
                new(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(10), ScriptPath: script, ExecutablePath: executable,
                    AdditionalArguments: ["-ProbePath", probe, "-ProbeConfig", config]), cancel.Token));
            try
            {
                // Cold EF/model startup is fixture setup under the unchanged 60s
                // case/child budget. The operation receipt still has exactly 5s.
                using (TestTiming.BeginPhase(TestPhaseKind.FixtureSetup, "host-load probe initialization"))
                    while (!File.Exists(initialized) && !owned.IsCompleted) await Task.Delay(5, ct);
                File.Exists(initialized).ShouldBeTrue(cut + ": probe failed during initialization");
                world.Progress("crash child initialized");
                using (var setup = JsonDocument.Parse(File.ReadAllText(initialized)))
                    setup.RootElement.GetProperty("nonce").GetString().ShouldBe(nonce);
                await C1514HostLoadWorld.EventuallyAsync(() => File.Exists(ready) || owned.IsCompleted, cancellationToken: ct);
                File.Exists(ready).ShouldBeTrue(cut + ": " + (owned.IsCompletedSuccessfully ? owned.Result.Stderr : "probe did not reach the selected boundary"));
                using var receipt = JsonDocument.Parse(File.ReadAllText(ready)); receipt.RootElement.GetProperty("nonce").GetString().ShouldBe(nonce);
                receipt.RootElement.GetProperty("source").GetProperty("sequence").GetInt64().ShouldBe(101);
                world.Progress("crash boundary receipt read");
            }
            finally
            {
                cancel.Cancel();
                try { await owned; } catch (OperationCanceledException) { }
            }
            if (cut.StartsWith("append-", StringComparison.Ordinal)) world.History = new HostLoadSpool(world.Runner.HistoryPath!, world.Runner, world.Clock);
            world.RecreateServices(); await world.TransferAsync();
            await using var fresh = world.Db(false); var rows = await fresh.HostLoadSamples.Where(r => r.Sequence == 101).ToListAsync();
            using var api = await world.GetAsync("/api/hosts/grok-linux/load/history?from=2026-11-06T12:00:00Z&to=2026-11-06T12:00:05Z");
            if (cut == "append-before-flush")
            { rows.ShouldBeEmpty(); api.RootElement.GetProperty("complete").GetBoolean().ShouldBeFalse(); api.RootElement.GetProperty("gaps").GetArrayLength().ShouldBeGreaterThan(0); }
            else
            {
                rows.Count.ShouldBe(1, cut); JsonSerializer.Deserialize<HostLoadSample>(rows[0].PayloadJson, PhoneHomeFraming.Json)!.CpuPercent.ShouldBe(40d);
                api.RootElement.GetProperty("items").GetArrayLength().ShouldBe(1, cut);
                var rollup = await fresh.HostLoadHourlyRollups.SingleAsync(r => r.HourUtc == C1514HostLoadWorld.T0 && r.Metric == "cpuPercent");
                var summary = JsonSerializer.Deserialize<HostLoadMetricSummary>(rollup.StatisticsJson, PhoneHomeFraming.Json)!;
                summary.ValidCount.ShouldBe(1, cut); summary.Sum.ShouldBe(40d, cut); summary.P95.ShouldBe(40d, cut);
                if (taskId is { } id)
                {
                    using var taskApi = await world.GetAsync("/api/agent-tasks/" + id + "/load");
                    var windows = taskApi.RootElement.GetProperty("windows"); windows.GetArrayLength().ShouldBe(1, cut);
                    var window = windows[0]; window.GetProperty("start").GetProperty("sampleIdentity").GetProperty("sequence").GetInt64().ShouldBe(101, cut);
                    window.GetProperty("end").GetProperty("sampleIdentity").GetProperty("sequence").GetInt64().ShouldBe(101, cut);
                    var cpu = window.GetProperty("during").GetProperty("cpuPercent");
                    cpu.GetProperty("validCount").GetInt64().ShouldBe(1, cut); cpu.GetProperty("sum").GetDouble().ShouldBe(40, cut);
                    cpu.GetProperty("min").GetDouble().ShouldBe(40, cut); cpu.GetProperty("max").GetDouble().ShouldBe(40, cut); cpu.GetProperty("p95").GetDouble().ShouldBe(40, cut);
                }
            }
        }
    }
}
