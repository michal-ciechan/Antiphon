using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Scripts;
using Microsoft.EntityFrameworkCore;
using Shouldly;
namespace Antiphon.Tests.TestHelpers;
internal static class C1514HostLoadCrashCases
{
    public static async Task RunAsync(C1514HostLoadFixture fixture)
    {
        var probe = Path.Combine(AppContext.BaseDirectory, "host-load-probe", "Antiphon.HostLoad.Probe.dll"); File.Exists(probe).ShouldBeTrue("The probe is staged by the checkpoint build, never built inside a test.");
        foreach (var cut in new[] { "append-before-flush", "append-after-flush", "ingest-before-enqueue", "ingest-after-enqueue", "ingest-before-save", "ingest-before-commit", "ingest-after-commit", "derive-before-call", "derive-before-save", "derive-after-write", "derive-after-commit" })
        {
            await using var world = await fixture.WorldAsync(phoneHome: true);
            HostLoadSample sample;
            if (cut.StartsWith("append-", StringComparison.Ordinal)) sample = new() { Source = new("grok-linux", world.Spool.RunnerStoreId, world.Spool.BootId, 101), SampledAt = C1514HostLoadWorld.T0,
                PhysicalHostKey = "owned-host", SourceEpoch = world.Runner.SourceEpoch, BindingRevision = 1, CpuPercent = 40, HostLogicalCores = 24, BusyCoreEquivalent = 9.6 };
            else { await world.SeedPrefixAsync(); sample = await world.ProduceAsync(C1514HostLoadWorld.T0); }
            var nonce = Guid.NewGuid().ToString("N"); var ready = Path.Combine(world.Root, "ready"); var config = Path.Combine(world.Root, "probe-config.json");
            File.WriteAllText(config, JsonSerializer.Serialize(new { world.Root, SourceUrl = world.ProxyUrl, ConnectionString = world.Schema.ConnectionString, Now = C1514HostLoadWorld.T0, Cut = cut, Nonce = nonce, ReadyPath = ready, Sample = sample }, PhoneHomeFraming.Json));
            var script = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "test-host-load.ps1"); using var cancel = new CancellationTokenSource();
            var owned = ScriptHarnessProcess.RunAsync("test-host-load.ps1", "c1514-crash", "Probe", script,
                new(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(10), ScriptPath: script, AdditionalArguments: ["-ProbePath", probe, "-ProbeConfig", config]), cancel.Token);
            try
            {
                await C1514HostLoadWorld.EventuallyAsync(() => File.Exists(ready) || owned.IsCompleted);
                File.Exists(ready).ShouldBeTrue(cut + ": " + (owned.IsCompletedSuccessfully ? owned.Result.Stderr : "probe did not reach the selected boundary"));
                using var receipt = JsonDocument.Parse(File.ReadAllText(ready)); receipt.RootElement.GetProperty("nonce").GetString().ShouldBe(nonce);
                receipt.RootElement.GetProperty("source").GetProperty("sequence").GetInt64().ShouldBe(101);
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
            }
        }
    }
}
