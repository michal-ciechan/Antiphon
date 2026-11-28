using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Shouldly;
using TUnit.Core;
namespace Antiphon.SessionRunner.Tests;

// Both native pairs overlap. The critical path is startup + about 6s warmup
// (an immediate tick, one 5s interval and 1s settling) + 21 * 5s measurement + cleanup.
// CARD-1514: 230s is provisionally about twice the expected wall time.
[Category("Integration"), Category("Slow"), Timeout(230_000), ParallelLimiter<ProcessSpawnLimit>]
[ClassDataSource<C1514HostLoadFixture>(Shared = SharedType.PerTestSession)]
public class C1514HostLoadNativeOverheadTests(C1514HostLoadFixture fixture)
{
    private const int WarmupTickCount = 2;
    private const int SteadyTickTarget = 21;
    private static readonly TimeSpan TickWorkSettleTime = TimeSpan.FromSeconds(1);

    [Test]
    [NativeLinuxOnly("CARD-1514: two native Linux full-runner off/on pairs; CP-7 requires measured CPU, cadence, tick and spool budgets.")]
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
#if C1514_SAMPLER_CALIBRATION
        var worstCpuPoints = double.NegativeInfinity;
#endif
        var loadBefore = HostLoad();
        var pairs = await Task.WhenAll(RunPairAsync(0), RunPairAsync(1));
        var loadAfter = HostLoad();
        for (var pair = 0; pair < pairs.Length; pair++)
        {
            var (offCpuPoints, on) = pairs[pair];
            var cpuPoints = on.CpuPoints - offCpuPoints;
            Console.WriteLine("C1514 overhead pair=" + pair + " cpuPoints=" + cpuPoints.ToString("R", CultureInfo.InvariantCulture)
                + " offCpuPoints=" + offCpuPoints.ToString("R", CultureInfo.InvariantCulture)
                + " onCpuPoints=" + on.CpuPoints.ToString("R", CultureInfo.InvariantCulture) + " samples=" + on.Samples
                + " measuredTicks=" + on.MeasuredTicks + " p95TickMs=" + on.P95TickMs.ToString("R", CultureInfo.InvariantCulture)
                + " spoolBytes=" + on.Bytes + " authorityRequests=" + on.Requests
                + " loadBefore=" + loadBefore + " loadAfter=" + loadAfter);
#if C1514_SAMPLER_CALIBRATION
            // Preserve both pairs when the intentional CPU regression crosses the budget.
            worstCpuPoints = Math.Max(worstCpuPoints, cpuPoints);
#else
            cpuPoints.ShouldBeLessThan(1d, "incremental CPU points of one core; pair=" + pair);
#endif
            on.Samples.ShouldBeGreaterThanOrEqualTo(12); on.P95TickMs.ShouldBeLessThan(25d);
            on.Bytes.ShouldBeLessThanOrEqualTo(256L * 1024 * 1024); on.Requests.ShouldBeLessThanOrEqualTo(on.TotalSamples);
        }
#if C1514_SAMPLER_CALIBRATION
        worstCpuPoints.ShouldBeLessThan(1d, "maximum incremental CPU points of one core across both pairs");
#endif
    }

    private async Task<(double OffCpuPoints, Measurement On)> RunPairAsync(int pair)
    {
        using var pairTrace = TestTrace.ChunkSpan("C1514 native pair", "pair", pair);
        // Each runner owns its root, spool, ephemeral port and authority route. Wait for both
        // before taking CPU snapshots so the comparison uses the same wall-clock window.
        await using var off = new NativeRunner(fixture.PrivatePath());
        await using var on = new NativeRunner(fixture.PrivatePath());
        var addresses = await Task.WhenAll(StartAsync(off, false, pair), StartAsync(on, true, pair));
        using var http = new HttpClient { BaseAddress = addresses[1], Timeout = TimeSpan.FromSeconds(3) };

        using (TestTrace.ChunkSpan("C1514 native warmup", "pair", pair))
        {
            // The first two logged ticks pay logger, spool and authority-refresh JIT.
            var warmup = await ReadTicksAsync(on.Ticks, WarmupTickCount, TimeSpan.FromSeconds(20));
            await Task.Delay(TickWorkSettleTime);
            Console.WriteLine("C1514 warmup pair=" + pair + " elapsedTickMs="
                + string.Join(",", warmup.Select(tick => tick.ElapsedMs.ToString("F3", CultureInfo.InvariantCulture))));
        }

        var offBefore = off.ReadCpuTime();
        var onBefore = on.ReadCpuTime();
        var from = DateTimeOffset.UtcNow;
        var windowStart = Stopwatch.GetTimestamp();
        List<Tick> ticks;
        using (TestTrace.ChunkSpan("C1514 native measurement", "pair", pair))
        {
            // Leave one tick of margin over the percentile's minimum and retain the old 120s
            // cadence deadline. The spool worker runs asynchronously, so sample CPU at the
            // same offset after the final tick as after warmup.
            ticks = new(await ReadTicksAsync(on.Ticks, SteadyTickTarget, TimeSpan.FromSeconds(120), windowStart));
            await Task.Delay(TickWorkSettleTime);
        }
        var windowEnd = Stopwatch.GetTimestamp();
        var to = DateTimeOffset.UtcNow;
        var elapsedSeconds = Stopwatch.GetElapsedTime(windowStart, windowEnd).TotalSeconds;
        var offCpu = (off.ReadCpuTime() - offBefore).TotalSeconds / elapsedSeconds * 100d;
        var onCpu = (on.ReadCpuTime() - onBefore).TotalSeconds / elapsedSeconds * 100d;
        while (on.Ticks.TryRead(out var tick)) ticks.Add(tick);
        var measured = ticks.Where(tick => tick.ObservedAt >= windowStart && tick.ObservedAt < windowEnd)
            .Select(tick => tick.ElapsedMs).Order().ToArray();
        measured.Length.ShouldBeGreaterThanOrEqualTo(20, "Steady ticks must be numerous enough that nearest-rank p95 excludes the slowest.");
        var index = (int)Math.Ceiling(.95 * measured.Length) - 1;
        index.ShouldBeLessThan(measured.Length - 1, "p95 must not be the single maximum tick");
        var page = (await http.GetFromJsonAsync<HostLoadHistoryPage>("/host-stats/history", PhoneHomeFraming.Json))!;
        var samples = page.Samples.Count(s => s.SampledAt >= from && s.SampledAt < to);
        var bytes = Directory.EnumerateFiles(Path.Combine(on.Root, "spool")).Sum(path => new FileInfo(path).Length);
        Console.WriteLine(FormattableString.Invariant($"C1514 window pair={pair} elapsedSeconds={elapsedSeconds:F3}"));
        return (offCpu, new(onCpu, samples, measured.Length, measured[index], bytes, on.Requests, page.Samples.Count));
    }

    private async Task<Uri> StartAsync(NativeRunner runner, bool enabled, int pair)
    {
        var root = runner.Root;
        Directory.CreateDirectory(root);
        var authority = await fixture.AuthorityAsync(runner.RespondToAuthorityAsync);
        var info = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var key in info.Environment.Keys.Where(k => k.StartsWith("SessionRunner__", StringComparison.OrdinalIgnoreCase)
            || k.StartsWith("PhoneHome__", StringComparison.OrdinalIgnoreCase) || k.StartsWith("ANTIPHON_", StringComparison.OrdinalIgnoreCase)).ToArray()) info.Environment.Remove(key);
        // Tiered compilation adds background CPU on the enabled path even after warmup.
        info.Environment["DOTNET_TieredCompilation"] = "0";
#if C1514_SAMPLER_CALIBRATION
        if (enabled && Environment.GetEnvironmentVariable(HostStatsSamplerService.CalibrationCpuVariable) is { } extraCpu)
            info.Environment[HostStatsSamplerService.CalibrationCpuVariable] = extraCpu;
#endif
        foreach (var arg in new[] { Path.Combine(AppContext.BaseDirectory, "Antiphon.SessionRunner.dll"), "--urls", "http://127.0.0.1:0",
            "--SessionRunner:SessionLogPath", root, "--SessionRunner:Herdr:Enabled", "false", "--SessionRunner:CpuWatchdogEnabled", "false",
            "--SessionRunner:BuildSlotsOnly", "false", "--PhoneHome:Enabled", "false", "--PhoneHome:CapacityStatePath", Path.Combine(root, "capacity"),
            "--SessionRunner:CodexCliVersion:Executable", Path.Combine(root, "missing-owned-cli"), "--SessionRunner:CodexCliVersion:ResolutionCwd", root,
            "--SessionRunner:HostStats:Enabled", enabled ? "true" : "false", "--SessionRunner:HostStats:HistoryEnabled", "true",
            "--SessionRunner:HostStats:HistoryPath", Path.Combine(root, "spool"), "--SessionRunner:HostStats:AuthorityEndpoint", authority,
            "--SessionRunner:HostStats:AuthorityId", "owned-authority", "--SessionRunner:HostStats:PhysicalHostKey", "owned-host",
            "--SessionRunner:HostStats:RunnerId", "owned-runner", "--Serilog:LogPath", Path.Combine(root, "logs"),
            "--Serilog:MinimumLevel:Override:Antiphon.SessionRunner.HostStatsSamplerService", "Debug" }) info.ArgumentList.Add(arg);
        using var startupTrace = TestTrace.ChunkSpan("C1514 native child startup", "enabled", enabled);
        startupTrace?.SetTag("pair", pair);
        var startup = Stopwatch.StartNew();
        var address = await runner.StartAsync(info);
        Console.WriteLine(FormattableString.Invariant($"C1514 startup pair={pair} enabled={enabled} elapsedMs={startup.Elapsed.TotalMilliseconds:F1}"));
        address.Port.ShouldNotBe(17204);
        address.Port.ShouldNotBe(8080);
        return address;
    }

    private static async Task<Tick[]> ReadTicksAsync(ChannelReader<Tick> ticks, int count, TimeSpan timeout, long notBefore = long.MinValue)
    {
        using var deadline = new CancellationTokenSource(timeout);
        var values = new Tick[count];
        var captured = 0;
        try
        {
            while (captured < count)
            {
                var tick = await ticks.ReadAsync(deadline.Token);
                if (tick.ObservedAt >= notBefore)
                    values[captured++] = tick;
            }
            return values;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException($"Observed {captured} of {count} native sampler ticks within {timeout}.");
        }
    }

    private static string HostLoad()
    {
        if (!OperatingSystem.IsLinux()) return "unavailable";
        var parts = File.ReadAllText("/proc/loadavg").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 ? parts[0] + " " + parts[1] + " " + parts[2] : "unavailable";
    }

    private sealed record Measurement(double CpuPoints, int Samples, int MeasuredTicks, double P95TickMs, long Bytes, int Requests, int TotalSamples);

    private readonly record struct Tick(long ObservedAt, double ElapsedMs);

    private sealed class NativeRunner(string root) : IAsyncDisposable
    {
        private readonly Process _process = new();
        private readonly TaskCompletionSource<Uri> _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Channel<Tick> _ticks = Channel.CreateUnbounded<Tick>(new UnboundedChannelOptions { SingleReader = true });
        private bool _started;
        private int _requests;

        public string Root { get; } = root;
        public ChannelReader<Tick> Ticks => _ticks.Reader;
        public int Requests => Volatile.Read(ref _requests);

        public async Task RespondToAuthorityAsync(HttpContext context)
        {
            Interlocked.Increment(ref _requests);
            await context.Response.WriteAsJsonAsync(new HostAdmissionSnapshot("owned-authority", "owned-host", 1, DateTimeOffset.UtcNow,
                "known", "Lease", 0, 4, 0, null, 6), context.RequestAborted);
        }

        public async Task<Uri> StartAsync(ProcessStartInfo info)
        {
            _process.StartInfo = info;
            _process.OutputDataReceived += (_, e) => Capture(e.Data);
            _process.ErrorDataReceived += (_, e) => Capture(e.Data);
            _started = _process.Start();
            _started.ShouldBeTrue();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            return await _listening.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }

        private void Capture(string? line)
        {
            if (line is null) return;
            var listening = Regex.Match(line, @"Now listening on:\s+(http://127\.0\.0\.1:\d+)");
            if (listening.Success) _listening.TrySetResult(new Uri(listening.Groups[1].Value));
            var tick = Regex.Match(line, @"HOST_LOAD_TICK elapsedMs=(\S+)");
            if (tick.Success && double.TryParse(tick.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var elapsedMs)
                && double.IsFinite(elapsedMs))
                _ticks.Writer.TryWrite(new(Stopwatch.GetTimestamp(), elapsedMs));
        }

        public TimeSpan ReadCpuTime()
        {
            _process.Refresh();
            return _process.TotalProcessorTime;
        }

        public async ValueTask DisposeAsync()
        {
            using (_process)
            {
                if (!_started) return;
                if (!_process.HasExited) _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                _process.WaitForExit(); // Join redirected event drains as well as the owned process.
            }
        }
    }
}
