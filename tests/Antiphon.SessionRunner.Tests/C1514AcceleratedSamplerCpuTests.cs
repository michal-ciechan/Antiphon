// Calibration builds set -p:DefineConstants=C1514_SAMPLER_CALIBRATION. Ordinary builds
// omit the test from discovery so the qualification roster needs no exemption.
// Launch a fresh, single-test host with DOTNET_TieredCompilation=0 to match the native arms.
#if C1514_SAMPLER_CALIBRATION
using System.Diagnostics;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration"), Timeout(55_000)]
[ClassDataSource<C1514HostLoadFixture>(Shared = SharedType.PerTestSession)]
public sealed class C1514AcceleratedSamplerCpuTests(C1514HostLoadFixture fixture)
{
    [Test]
    public async Task Windows_AcceleratedSamplerStaysWithinCpuBudget(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("ANTIPHON_C1514_SAMPLER_CALIBRATION") != "1")
        {
            Skip.Test("Windows calibration only. Select this method alone and set ANTIPHON_C1514_SAMPLER_CALIBRATION=1.");
            return;
        }

        var spool = fixture.PrivatePath();
        Directory.CreateDirectory(spool);
        var clock = new AcceleratedSamplerClock();
        var authority = await fixture.AuthorityAsync(context =>
            context.Response.WriteAsJsonAsync(new HostAdmissionSnapshot("owned-authority", "owned-host", 1,
                clock.GetUtcNow(), "known", "Lease", 0, 4, 0, null, 6), context.RequestAborted)).WaitAsync(ct);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SessionRunner:SessionLogPath"] = spool,
            ["SessionRunner:HostStats:HistoryPath"] = spool,
            ["SessionRunner:HostStats:AuthorityEndpoint"] = authority,
            ["SessionRunner:HostStats:AuthorityId"] = "owned-authority",
            ["SessionRunner:HostStats:PhysicalHostKey"] = "owned-host",
            ["SessionRunner:HostStats:RunnerId"] = "owned-runner",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddSimpleConsole()
            .AddFilter(typeof(HostStatsSamplerService).FullName, LogLevel.Debug)
            .AddFilter("System.Net.Http.HttpClient", LogLevel.Warning));
        services.AddSingleton<TimeProvider>(clock);
        services.AddHostStats(configuration);
        await using var provider = services.BuildServiceProvider();
        var capture = provider.GetRequiredService<HostLoadCaptureService>();
        var sampler = provider.GetRequiredService<HostStatsSamplerService>();

        try
        {
            await capture.StartAsync(ct);
            await sampler.StartAsync(ct);
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            var before = capture.Diagnostics;
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var cpuBefore = process.TotalProcessorTime;
            var wall = Stopwatch.StartNew();
            await Task.Delay(TimeSpan.FromSeconds(25), ct);
            process.Refresh();
            var cpuMs = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
            var elapsedMs = wall.Elapsed.TotalMilliseconds;
            var after = capture.Diagnostics;
            var appended = after.Appended - before.Appended;

            // Keep the real 5 s configuration and spool schema. Count actual completed appends
            // so a late timer cannot make the estimated cost per sample artificially low.
            appended.ShouldBeGreaterThanOrEqualTo(45);
            after.Dropped.ShouldBe(0);
            after.AuthorityUnknown.ShouldBe(0);
            // Charge all test-host CPU, including scans, async capture, GC and the loopback
            // authority server. This bounds the pipeline's CPU inside this isolated host.
            var points = 100 * cpuMs / (appended * (double)new HostStatsSettings().IntervalMs);
            Console.WriteLine(FormattableString.Invariant(
                $"C1514 accelerated cpuPoints={points:R} cpuMs={cpuMs:R} wallMs={elapsedMs:R} appends={appended}"));
            points.ShouldBeLessThan(1d, "normalized CPU points of one core at the production cadence");
        }
        finally
        {
            using var stopDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await sampler.StopAsync(stopDeadline.Token); }
            finally { await capture.StopAsync(stopDeadline.Token); }
        }
    }

    private sealed class AcceleratedSamplerClock : TimeProvider
    {
        private const int Speed = 10;

        public override long GetTimestamp() => System.GetTimestamp() * Speed;
        public override long TimestampFrequency => System.TimestampFrequency;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => System.CreateTimer(callback, state, AccelerateCadence(dueTime), AccelerateCadence(period));

        // Preserve the authority HTTP deadline and use the system UTC clock for persisted timestamps.
        private static TimeSpan AccelerateCadence(TimeSpan duration) =>
            duration == TimeSpan.FromSeconds(5) ? duration / Speed : duration;
    }
}
#endif
