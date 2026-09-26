using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
public class HostStatsEndpointTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset T0 = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Host_stats_answers_newest_sample_and_one_minute_rollups()
    {
        var time = new FakeTimeProvider(T0);
        var probe = new ScriptedHostStatsProbe { Cpu = 10 };
        await using var host = await HostStatsTestHost.StartAsync(Enabled(), probe, time);
        await host.Sampler.SampleOnceAsync();
        time.Advance(TimeSpan.FromSeconds(5));
        probe.Cpu = 30;
        await host.Sampler.SampleOnceAsync();

        var dto = await host.Http.GetFromJsonAsync<RunnerHostStatsDto>("host-stats", Json);
        dto.ShouldNotBeNull();
        dto.At.ShouldBe(time.GetUtcNow());
        dto.Rollups["1m"].CpuPercent.ShouldNotBeNull();
        dto.Rollups["1m"].CpuPercent!.Avg.ShouldBe(20);
        dto.Rollups["1m"].CpuPercent.Max.ShouldBe(30);
    }

    [Test]
    public async Task Series_route_answers_points_in_window()
    {
        var time = new FakeTimeProvider(T0);
        var probe = new ScriptedHostStatsProbe { Cpu = 10 };
        await using var host = await HostStatsTestHost.StartAsync(Enabled(), probe, time);
        await host.Sampler.SampleOnceAsync();
        time.Advance(TimeSpan.FromMinutes(6));
        probe.Cpu = 20;
        await host.Sampler.SampleOnceAsync();
        time.Advance(TimeSpan.FromSeconds(5));
        probe.Cpu = 30;
        await host.Sampler.SampleOnceAsync();

        var five = await host.Http.GetFromJsonAsync<RunnerHostSeriesDto>("host-stats/series?metric=cpu&window=5m", Json);
        five.ShouldNotBeNull();
        five.Window.ShouldBe("5m");
        five.Points.Count.ShouldBe(2);
        five.Points[1].T.ShouldBeGreaterThan(five.Points[0].T);

        var thirty = await host.Http.GetFromJsonAsync<RunnerHostSeriesDto>("host-stats/series?metric=cpu&window=30m", Json);
        thirty.ShouldNotBeNull();
        thirty.Points.Count.ShouldBe(3);
    }

    [Test]
    public async Task Bad_metric_or_window_is_400_problem()
    {
        await using var host = await HostStatsTestHost.StartAsync(Enabled(), new ScriptedHostStatsProbe(), new FakeTimeProvider(T0));
        foreach (var path in new[] { "host-stats/series?metric=bogus&window=1m", "host-stats/series?metric=cpu&window=2h" })
        {
            var response = await host.Http.GetAsync(path);
            ((int)response.StatusCode).ShouldBe(400);
            response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        }
    }

    [Test]
    public async Task Disabled_host_stats_is_404()
    {
        var settings = Enabled();
        settings["SessionRunner:HostStats:Enabled"] = "false";
        await using var host = await HostStatsTestHost.StartAsync(settings, new ScriptedHostStatsProbe(), new FakeTimeProvider(T0));
        ((int)(await host.Http.GetAsync("host-stats")).StatusCode).ShouldBe((int)HttpStatusCode.NotFound);
        ((int)(await host.Http.GetAsync("host-stats/series?metric=cpu&window=1m")).StatusCode).ShouldBe((int)HttpStatusCode.NotFound);
    }

    private static Dictionary<string, string?> Enabled() => new()
    {
        ["SessionRunner:HostStats:Enabled"] = "true",
        ["SessionRunner:HostStats:IntervalMs"] = "5000",
        ["SessionRunner:HostStats:RetentionMinutes"] = "30",
        ["SessionRunner:HostStats:ProcessSampling"] = "false",
    };
}
