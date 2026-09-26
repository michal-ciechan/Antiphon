using System.Net;
using System.Text.Json;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class HostStatsEndpointTests
{
    private sealed class EmptyCounters : IHostStatsAntiphonCounters
    {
        public Task<IReadOnlyDictionary<string, HostStatsAntiphonDto>> ReadAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, HostStatsAntiphonDto>>(new Dictionary<string, HostStatsAntiphonDto>());
    }

    private static async Task<(PhoneHomeTestHost Host, HostStatsCache Cache, FakeTimeProvider Time)> StartAsync(
        string? connectionString = null)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero));
        var cache = new HostStatsCache(new HostStatsSettings(), time);
        var host = await PhoneHomeTestHost.StartAsync(clock: time, connectionString: connectionString,
            configureServices: services =>
            {
                services.AddSingleton(cache);
                services.AddSingleton<TimeProvider>(time);
                services.AddSingleton(Options.Create(new HostStatsSettings()));
            }, mapEndpoints: app => app.MapHostStatsEndpoints());
        return (host, cache, time);
    }

    private static PhoneHomeFrame Reply(PhoneHomeFrame request, object dto) =>
        new(PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation,
            JsonSerializer.SerializeToElement(dto, PhoneHomeFraming.Json));

    private static HostStatsPollService Poll(PhoneHomeTestHost host, HostStatsCache cache, FakeTimeProvider time,
        IHostStatsAntiphonCounters? counters = null) =>
        new(host.Directory, cache, counters ?? new EmptyCounters(), new RecordingHostStatsEventBus(),
            Options.Create(new HostStatsSettings()), time, NullLogger<HostStatsPollService>.Instance);

    [Test]
    public async Task Hosts_stats_answers_the_cache_projection_with_counters()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (host, cache, time) = await StartAsync(schema.ConnectionString);
        await using (host)
        {
            host.Local.HostStats = HostStatsCacheTests.Sample(cpu: 25);
            await using var peer = await host.ConnectPeerAsync();
            host.Directory.MarkRecovered(await host.WaitLiveAsync());
            peer.Reply = frame => frame.Operation == PhoneHomeOperation.HostStats
                ? Reply(frame, HostStatsCacheTests.Sample(cpu: 42)) : null;
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                db.AgentTasks.Add(TaskRow(host.AllowedRunnerId));
                db.AgentTasks.Add(TaskRow(null));
                await db.SaveChangesAsync();
            }
            var counters = new HostStatsAntiphonCounters(host.App.Services.GetRequiredService<IServiceScopeFactory>());
            await Poll(host, cache, time, counters).TickOnceAsync();
            using var response = await host.Http.GetAsync("/api/hosts/stats");
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var rows = json.RootElement.EnumerateArray().ToArray();
            rows.Length.ShouldBe(2);
            foreach (var row in rows)
            {
                row.GetProperty("state").GetString().ShouldBe("live");
                row.GetProperty("current").GetProperty("cpuPercent").GetDouble().ShouldBeGreaterThan(0);
                row.GetProperty("rollups").GetProperty("1m").GetProperty("cpuPercent").GetProperty("avg").GetDouble()
                    .ShouldBeGreaterThan(0);
                row.GetProperty("antiphon").GetProperty("tasksInFlight").GetInt32().ShouldBe(1);
            }
        }
    }

    [Test]
    public async Task Series_proxies_the_peer_operation_30_answer()
    {
        var (host, _, _) = await StartAsync();
        await using (host)
        {
            await using var peer = await host.ConnectPeerAsync();
            host.Directory.MarkRecovered(await host.WaitLiveAsync());
            peer.Reply = frame => frame.Operation == PhoneHomeOperation.HostStatsSeries
                ? Reply(frame, new RunnerHostSeriesDto("cpu", "30m", 5,
                    [new(DateTimeOffset.UtcNow, 1), new(DateTimeOffset.UtcNow.AddSeconds(5), 2),
                        new(DateTimeOffset.UtcNow.AddSeconds(10), 3)])) : null;
            using var response = await host.Http.GetAsync($"/api/hosts/{host.AllowedRunnerId}/stats/series?metric=cpu&window=30m");
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            json.RootElement.GetProperty("points").GetArrayLength().ShouldBe(3);
            peer.RequestCount(PhoneHomeOperation.HostStatsSeries).ShouldBe(1);
            var request = await peer.WaitForAsync(PhoneHomeOperation.HostStatsSeries);
            request.Payload!.Value.GetProperty("metric").GetString().ShouldBe("cpu");
            request.Payload!.Value.GetProperty("window").GetString().ShouldBe("30m");
        }
    }

    [Test]
    public async Task Unknown_host_is_404()
    {
        var (host, _, _) = await StartAsync();
        await using (host)
        {
            using var response = await host.Http.GetAsync("/api/hosts/nope/stats/series?metric=cpu&window=1m");
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            host.Local.Calls.ShouldNotContain("series:cpu:1m");
        }
    }

    [Test]
    public async Task Bad_window_is_400_before_any_runner_call()
    {
        var (host, _, _) = await StartAsync();
        await using (host)
        {
            await using var peer = await host.ConnectPeerAsync();
            foreach (var query in new[] { "metric=cpu&window=2h", "metric=bogus&window=1m", "metric=tasks&window=1m" })
            {
                using var response = await host.Http.GetAsync($"/api/hosts/{host.AllowedRunnerId}/stats/series?{query}");
                response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            }
            peer.RequestCount(PhoneHomeOperation.HostStatsSeries).ShouldBe(0);
            host.Local.Calls.ShouldNotContain("series:cpu:2h");
        }
    }

    [Test]
    public async Task Series_for_a_disconnected_runner_is_409_unavailable()
    {
        var (host, _, _) = await StartAsync();
        await using (host)
        {
            using var response = await host.Http.GetAsync($"/api/hosts/{host.AllowedRunnerId}/stats/series?metric=cpu&window=1m");
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            json.RootElement.GetProperty("code").GetString().ShouldBe(PhoneHomeProblemTypes.Unavailable);
        }
    }

    private static AgentTask TaskRow(string? runnerId)
    {
        var id = Guid.NewGuid();
        return new AgentTask
        {
            Id = id, RootTaskId = id, Title = "host stats", Goal = "host stats",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, AgentKind = AgentKind.Grok,
            ModelLevel = AgentModelLevel.Medium, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = "/work", RunnerId = runnerId, Status = AgentTaskStatus.Working,
            ReplyTo = AgentTaskReplyTo.None, CreatedAt = DateTime.UtcNow, ConcurrencyToken = Guid.NewGuid(),
        };
    }
}
