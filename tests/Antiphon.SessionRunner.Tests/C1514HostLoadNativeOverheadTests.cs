using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport;
using Microsoft.AspNetCore.Http;
using Shouldly;
using TUnit.Core;
namespace Antiphon.SessionRunner.Tests;

[Category("Integration"), Category("Slow"), Timeout(360_000), ParallelLimiter<ProcessSpawnLimit>]
[ClassDataSource<C1514HostLoadFixture>(Shared = SharedType.PerTestSession)]
public class C1514HostLoadNativeOverheadTests(C1514HostLoadFixture fixture)
{
    [Test, NativeLinuxOnly("CARD-1514: two native Linux full-runner off/on pairs; CP-7 requires measured CPU, cadence, tick and spool budgets.")]
    public async Task Linux_SamplerOverheadStaysWithinBudget()
    { OperatingSystem.IsLinux().ShouldBeTrue(); await MeasureAsync(); }

    [Test]
    public async Task Windows_SamplerOverheadStaysWithinBudget()
    {
        if (!OperatingSystem.IsWindows()) { Skip.Test("CARD-1514: Windows full-runner overhead experiment; CP-8 required on Windows."); return; }
        await MeasureAsync();
    }
    private async Task MeasureAsync()
    {
        for (var pair = 0; pair < 2; pair++)
        {
            var off = await RunAsync(false); var on = await RunAsync(true);
            (on.CpuPoints - off.CpuPoints).ShouldBeLessThan(1d, "incremental CPU points of one core; pair=" + pair);
            on.Samples.ShouldBeGreaterThanOrEqualTo(12); on.P95TickMs.ShouldBeLessThan(25d);
            on.Bytes.ShouldBeLessThanOrEqualTo(256L * 1024 * 1024); on.Requests.ShouldBeLessThanOrEqualTo(on.TotalSamples);
            Console.WriteLine("C1514 overhead pair=" + pair + " offCpuPoints=" + off.CpuPoints.ToString("R", CultureInfo.InvariantCulture)
                + " onCpuPoints=" + on.CpuPoints.ToString("R", CultureInfo.InvariantCulture) + " samples=" + on.Samples + " p95TickMs=" + on.P95TickMs.ToString("R", CultureInfo.InvariantCulture)
                + " spoolBytes=" + on.Bytes + " authorityRequests=" + on.Requests);
        }
    }
    private async Task<Measurement> RunAsync(bool enabled)
    {
        var root = fixture.PrivatePath(); Directory.CreateDirectory(root); var requests = 0;
        var authority = await fixture.AuthorityAsync(async context =>
        {
            Interlocked.Increment(ref requests);
            await context.Response.WriteAsJsonAsync(new HostAdmissionSnapshot("owned-authority", "owned-host", 1, DateTimeOffset.UtcNow,
                "known", "Lease", 0, 4, 0, null, 6), context.RequestAborted);
        });
        var info = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var key in info.Environment.Keys.Where(k => k.StartsWith("SessionRunner__", StringComparison.OrdinalIgnoreCase)
            || k.StartsWith("PhoneHome__", StringComparison.OrdinalIgnoreCase) || k.StartsWith("ANTIPHON_", StringComparison.OrdinalIgnoreCase)).ToArray()) info.Environment.Remove(key);
        foreach (var arg in new[] { Path.Combine(AppContext.BaseDirectory, "Antiphon.SessionRunner.dll"), "--urls", "http://127.0.0.1:0",
            "--SessionRunner:SessionLogPath", root, "--SessionRunner:Herdr:Enabled", "false", "--SessionRunner:CpuWatchdogEnabled", "false",
            "--SessionRunner:BuildSlotsOnly", "false", "--PhoneHome:Enabled", "false", "--PhoneHome:CapacityStatePath", Path.Combine(root, "capacity"),
            "--SessionRunner:CodexCliVersion:Executable", Path.Combine(root, "missing-owned-cli"), "--SessionRunner:CodexCliVersion:ResolutionCwd", root,
            "--SessionRunner:HostStats:Enabled", enabled ? "true" : "false", "--SessionRunner:HostStats:HistoryEnabled", "true",
            "--SessionRunner:HostStats:HistoryPath", Path.Combine(root, "spool"), "--SessionRunner:HostStats:AuthorityEndpoint", authority,
            "--SessionRunner:HostStats:AuthorityId", "owned-authority", "--SessionRunner:HostStats:PhysicalHostKey", "owned-host",
            "--SessionRunner:HostStats:RunnerId", "owned-runner", "--Serilog:LogPath", Path.Combine(root, "logs"),
            "--Serilog:MinimumLevel:Override:Antiphon.SessionRunner.HostStatsSamplerService", "Debug" }) info.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = info }; var lines = new ConcurrentQueue<string>();
        var started = false;
        var listening = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Capture(string? line)
        {
            if (line is null) return; lines.Enqueue(line);
            var match = Regex.Match(line, @"Now listening on:\s+(http://127\.0\.0\.1:\d+)");
            if (match.Success) listening.TrySetResult(new Uri(match.Groups[1].Value));
        }
        process.OutputDataReceived += (_, e) => Capture(e.Data); process.ErrorDataReceived += (_, e) => Capture(e.Data);
        try
        {
            started = process.Start(); started.ShouldBeTrue(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
            var address = await listening.Task.WaitAsync(TimeSpan.FromSeconds(20)); address.Port.ShouldNotBe(17204); address.Port.ShouldNotBe(8080);
            using var http = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(3) };
            await Task.Delay(TimeSpan.FromSeconds(15));
            process.Refresh(); var before = process.TotalProcessorTime; var from = DateTimeOffset.UtcNow; var stopwatch = Stopwatch.StartNew();
            await Task.Delay(TimeSpan.FromSeconds(60));
            process.Refresh(); var cpu = (process.TotalProcessorTime - before).TotalSeconds / stopwatch.Elapsed.TotalSeconds * 100d; var to = DateTimeOffset.UtcNow;
            if (!enabled) return new(cpu, 0, 0, 0, 0, 0);
            var page = (await http.GetFromJsonAsync<HostLoadHistoryPage>("/host-stats/history", PhoneHomeFraming.Json))!;
            var samples = page.Samples.Count(s => s.SampledAt >= from && s.SampledAt < to);
            var ticks = lines.Select(line => Regex.Match(line, @"HOST_LOAD_TICK elapsedMs=([0-9.]+)"))
                .Where(m => m.Success).Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Order().ToArray();
            ticks.Length.ShouldBeGreaterThanOrEqualTo(12, "Production sampler debug timings must be observed, not synthesized.");
            var p95 = ticks[(int)Math.Ceiling(.95 * ticks.Length) - 1];
            var bytes = Directory.EnumerateFiles(Path.Combine(root, "spool")).Sum(path => new FileInfo(path).Length);
            return new(cpu, samples, p95, bytes, Volatile.Read(ref requests), page.Samples.Count);
        }
        finally
        {
            if (started)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                process.WaitForExit(); // Join redirected event drains as well as the owned process.
            }
        }
    }
    private sealed record Measurement(double CpuPoints, int Samples, double P95TickMs, long Bytes, int Requests, int TotalSamples);
}
