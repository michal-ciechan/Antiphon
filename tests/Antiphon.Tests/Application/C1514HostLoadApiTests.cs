using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
namespace Antiphon.Tests.Application;
[Category("Integration"), NotInParallel("C1514HostLoadServer")]
[ClassDataSource<C1514HostLoadFixture>(Shared = SharedType.PerTestSession)]
public class C1514HostLoadApiTests(C1514HostLoadFixture fixture)
{
    [Test, Timeout(10_000)]
    public async Task RoutesReturnDurableProvenanceAndPages()
    {
        await using var world = await fixture.WorldAsync(); var sample = await world.ProduceAsync(C1514HostLoadWorld.T0); await world.TransferAsync();
        var from = C1514HostLoadWorld.T0.AddHours(-3); var to = C1514HostLoadWorld.T0.AddSeconds(5);
        await using var late = world.Db(false); await using var transaction = await late.Database.BeginTransactionAsync();
        late.HostLoadSamples.Add(new() { RunnerStoreId = sample.Source.RunnerStoreId, BootId = sample.Source.BootId, Sequence = 9999,
            RunnerId = "grok-linux", PhysicalHostKey = "owned-host", SourceEpoch = sample.SourceEpoch, IsPrimary = true, SampledAt = to.AddSeconds(-1), IngestedAt = C1514HostLoadWorld.T0,
            PayloadJson = JsonSerializer.Serialize(sample with { Source = sample.Source with { Sequence = 9999 }, SampledAt = to.AddSeconds(-1) }, PhoneHomeFraming.Json) });
        await late.SaveChangesAsync();
        for (var offset = 2; offset <= 2001; offset += 120)
        {
            var points = Enumerable.Range(offset, Math.Min(120, 2002 - offset)).Select(i => sample with { Source = sample.Source with { Sequence = i }, SampledAt = from.AddSeconds((i - 2) * 5) }).ToArray();
            await using var db = world.Db(false); var after = (await db.HostLoadIngestCursors.SingleAsync()).Cursor;
            world.Pump.TryEnqueue(new("grok-linux", after, new(1, sample.Source, points, [], null, false, true))).ShouldBeTrue(); await world.Pump.ProcessOneAsync(CancellationToken.None);
        }
        var query = "/api/hosts/grok-linux/load/history?from=" + Uri.EscapeDataString(from.ToString("O")) + "&to=" + Uri.EscapeDataString(to.ToString("O"));
        using var first = await world.GetAsync(query + "&limit=2000"); var items = first.RootElement.GetProperty("items").EnumerateArray().ToArray(); items.Length.ShouldBe(2000);
        items[0].GetProperty("ingestedAt").GetDateTimeOffset().ShouldBe(C1514HostLoadWorld.T0); items[0].GetProperty("counters").GetProperty("tasksInFlight").ValueKind.ShouldBe(JsonValueKind.Null);
        var cursor = first.RootElement.GetProperty("nextCursor").GetString()!; await transaction.CommitAsync();
        using var second = await world.GetAsync(query + "&limit=2000&cursor=" + Uri.EscapeDataString(cursor));
        var rest = second.RootElement.GetProperty("items").EnumerateArray().ToArray(); rest.Length.ShouldBe(1); rest[0].GetProperty("source").GetProperty("sequence").GetInt64().ShouldBe(1);
        items.Concat(rest).Select(x => x.GetProperty("source").GetProperty("sequence").GetInt64()).Distinct().Count().ShouldBe(2001);
        using var standard = await world.GetAsync(query); standard.RootElement.GetProperty("items").GetArrayLength().ShouldBe(720);
        using var clamped = await world.GetAsync(query + "&limit=2001"); clamped.RootElement.GetProperty("items").GetArrayLength().ShouldBe(2000);
        foreach (var suffix in new[] { "?limit=0", "?limit=garbage", "?from=garbage", "?resolution=week", "?from=2026-01-01T00:00:00Z&to=2026-11-01T00:00:00Z", "?from=2026-11-06T12:00:00Z&to=2026-11-06T12:00:00Z", "?cursor=broken" })
            (await world.Http.GetAsync("/api/hosts/grok-linux/load/history" + suffix)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await world.Http.GetAsync(query.Replace("grok-linux", "desktop", StringComparison.Ordinal) + "&cursor=" + Uri.EscapeDataString(cursor))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await world.Http.GetAsync(query + "&resolution=hour&cursor=" + Uri.EscapeDataString(cursor))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await world.Http.GetAsync("/api/hosts/unknown-owned-host/load")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
    [Test, Timeout(10_000)]
    public async Task CurrentAndTaskReadsRespectExistingScope()
    {
        await using var world = await fixture.WorldAsync(); await world.SeedPrefixAsync(); await world.ProduceAsync(C1514HostLoadWorld.T0);
        var a = await world.StartTaskAsync(C1514HostLoadWorld.T0.AddSeconds(-1)); var b = await world.StartTaskAsync(C1514HostLoadWorld.T0.AddSeconds(-2)); await world.TransferAsync();
        await world.FinishTaskAsync(a.Id, C1514HostLoadWorld.T0.AddSeconds(1)); await world.RetryThroughServiceAsync(a.Id);
        await world.StartTaskAsync(C1514HostLoadWorld.T0.AddSeconds(2), attempt: 2, taskId: a.Id); await world.DeriveAsync();
        foreach (var token in new string?[] { null, "owned-stale-token", "owned-task-token" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/agent-tasks/" + a.Id + "/load?attempt=1"); if (token is not null) request.Headers.Add("X-Antiphon-Task-Token", token);
            using var response = await world.Http.SendAsync(request); response.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); json.RootElement.GetProperty("taskId").GetGuid().ShouldBe(a.Id); json.RootElement.GetProperty("attempt").GetInt32().ShouldBe(1);
            json.RootElement.GetProperty("windows").EnumerateArray().ShouldAllBe(w => w.GetProperty("taskId").GetGuid() == a.Id && w.GetProperty("attempt").GetInt32() == 1);
        }
        using var current = await world.GetAsync("/api/agent-tasks/" + a.Id + "/load"); current.RootElement.GetProperty("attempt").GetInt32().ShouldBe(2);
        using var other = await world.GetAsync("/api/agent-tasks/" + b.Id + "/load"); other.RootElement.GetProperty("taskId").GetGuid().ShouldBe(b.Id);
        (await world.Http.GetAsync("/api/agent-tasks/" + a.Id + "/load?attempt=0")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var load = await world.GetAsync("/api/hosts/grok-linux/load"); load.RootElement.GetProperty("current").GetProperty("source").GetProperty("sequence").GetInt64().ShouldBe(101);
    }
    [Test, Timeout(10_000)]
    public async Task OldPeersRemainLiveOnly()
    {
        foreach (var phoneHome in new[] { false, true })
        {
            await using var world = await fixture.WorldAsync(phoneHome); await world.ProduceAsync(C1514HostLoadWorld.T0); world.OldPeer = true;
            (await world.Source.GetHostStatsAsync(CancellationToken.None))!.Current!.CpuPercent.ShouldBe(40d);
            await Should.ThrowAsync<HostStatsUnsupportedException>(() => world.Source.GetHostLoadHistoryAsync(null, 120, CancellationToken.None));
            await using var db = world.Db(false); (await db.HostLoadIngestCursors.CountAsync()).ShouldBe(0);
            using var api = await world.GetAsync("/api/hosts/grok-linux/load/history"); api.RootElement.GetProperty("complete").GetBoolean().ShouldBeFalse(); api.RootElement.GetProperty("items").GetArrayLength().ShouldBe(0);
        }
        await using var broken = await fixture.WorldAsync(); broken.RunnerReply = async c => { c.Response.ContentType = "application/json"; await c.Response.WriteAsync("{truncated", c.RequestAborted); };
        await Should.ThrowAsync<JsonException>(() => broken.Source.GetHostLoadHistoryAsync(null, 120, CancellationToken.None));
        using var canceled = new CancellationTokenSource(); canceled.Cancel(); await Should.ThrowAsync<OperationCanceledException>(() => broken.Source.GetHostLoadHistoryAsync(null, 120, canceled.Token));
    }
    [Test, Timeout(10_000)]
    public async Task ShadowProjectionCannotChangeAdmission()
    {
        await using var world = await fixture.WorldAsync(); await using var db = world.Db(false);
        var options = Options.Create(new DelegationSettings { MaxConcurrentTasks = 8 });
        var budgets = new HostBudgetService(db, world.Directory, options, world.Clock); await budgets.UpsertAsync("grok-linux", 1, "owned telemetry witness", CancellationToken.None);
        using var process = Process.GetCurrentProcess();
        var broker = new BuildSlotBroker(Options.Create(new BuildSlotSettings { MaxConcurrent = 1, MinAvailableMemoryMb = 0 }), new SystemProcessLivenessProbe(), new Memory(), world.Clock, NullLogger<BuildSlotBroker>.Instance);
        broker.TryAcquire(new(process.Id, process.StartTime.ToUniversalTime(), "owned shadow test"), out _).ShouldBeTrue();
        var baseline = world.Directory.Status("grok-linux");
        foreach (var cpu in new[] { 5d, 95d, double.NaN })
        {
            await world.ProduceAsync(C1514HostLoadWorld.T0.AddSeconds(cpu == 5 ? 0 : cpu == 95 ? 5 : 10), cpu); await world.TransferAsync();
            using var read = await world.GetAsync("/api/hosts/grok-linux/load"); read.RootElement.GetProperty("policy").GetProperty("shadow").GetBoolean().ShouldBeTrue();
            var after = world.Directory.Status("grok-linux"); after.Available.ShouldBe(baseline.Available); after.DispatchEligible.ShouldBe(baseline.DispatchEligible); after.Draining.ShouldBe(baseline.Draining);
            broker.List().Budget.ShouldBe(1); broker.List().Occupied.ShouldBe(1); (await budgets.EffectiveAsync("grok-linux", CancellationToken.None)).Effective.ShouldBe(1);
            var pipeline = new AgentTaskPipelineStatusService(db, options, new AreaMapLoader(options, NullLogger<AreaMapLoader>.Instance), world.Clock,
                budgets: budgets, directory: world.Directory, hostLoad: world.ApiServices.GetRequiredService<HostLoadShadowCache>());
            var result = await pipeline.GetAsync(CancellationToken.None); result.Hosts.Single(h => h.HostId == "grok-linux").EffectiveLimit.ShouldBe(1);
        }
        (await db.AgentTasks.CountAsync()).ShouldBe(0);
    }
    private sealed class Memory : IHostMemoryProbe { public long? AvailableBytes => long.MaxValue; }
}
