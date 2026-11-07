using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.HostLoad;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
namespace Antiphon.Tests.Application;
[Category("Integration"), NotInParallel("C1514HostLoadServer"), ParallelLimiter<ProcessSpawnLimit>]
[ClassDataSource<C1514HostLoadFixture>(Shared = SharedType.PerTestSession)]
public class C1514HostLoadHistoryTests(C1514HostLoadFixture fixture)
{
    [Before(Class)]
    public static Task WarmDatabase() => TestDbFixture.Lifecycle.EnsureReadyAsync();
    [Test, Timeout(60_000)]
    public async Task ReadyRecipientReadsDurableSample()
    {
        foreach (var phoneHome in new[] { false, true })
        {
            await using var world = await fixture.WorldAsync(phoneHome); await world.SeedPrefixAsync();
            using var consumerStop = new CancellationTokenSource();
            // The real channel consumer is waiting before K=101 exists.
            var recipient = world.Pump.ProcessOneAsync(consumerStop.Token);
            HostLoadSample sample;
            try
            {
                sample = await world.ProduceAsync(C1514HostLoadWorld.T0); sample.Source.Sequence.ShouldBe(101);
                (await world.Pump.PollOnceAsync("grok-linux", CancellationToken.None)).ShouldBeTrue();
                await recipient; await world.DeriveAsync();
            }
            finally
            {
                consumerStop.Cancel();
                try { await recipient; } catch (OperationCanceledException) { }
            }
            await using var fresh = world.Db(false);
            var row = await fresh.HostLoadSamples.SingleAsync(r => r.RunnerStoreId == sample.Source.RunnerStoreId && r.BootId == sample.Source.BootId && r.Sequence == 101);
            JsonSerializer.Deserialize<HostLoadSample>(row.PayloadJson, PhoneHomeFraming.Json)!.CpuPercent.ShouldBe(40d);
            using var read = await world.GetAsync("/api/hosts/grok-linux/load/history?from=2026-11-06T12:00:00Z&to=2026-11-06T12:00:05Z");
            var received = read.RootElement.GetProperty("items").EnumerateArray().Single();
            received.GetProperty("source").GetProperty("sequence").GetInt64().ShouldBe(101);
            received.GetProperty("cpuPercent").GetDouble().ShouldBe(40d);
            var script = await Scripts.C1514HostLoadScriptTests.RunAsync(world, json: true);
            script.ExitCode.ShouldBe(0, script.Stderr); using var output = JsonDocument.Parse(script.Stdout);
            output.RootElement.GetProperty("current").GetProperty("source").GetProperty("sequence").GetInt64().ShouldBe(101);
            output.RootElement.GetProperty("current").GetProperty("cpuPercent").GetDouble().ShouldBe(40d);
            output.RootElement.GetProperty("recommendation").GetProperty("recommendedAdditionalTasks").GetInt32().ShouldBe(0);
        }
    }
    [Test, Timeout(60_000)]
    public async Task BusyRecipientEventuallyReadsSameSample()
    {
        foreach (var phoneHome in new[] { false, true })
        {
            await using var world = await fixture.WorldAsync(phoneHome); await world.SeedPrefixAsync();
            await world.ProduceAsync(C1514HostLoadWorld.T0.AddSeconds(-5)); await world.TransferAsync();
            await world.ProduceAsync(C1514HostLoadWorld.T0);
            world.ReadLimit = 1;
            var gate = new C1514HostLoadSqlProbe { Hold = sql => sql.Contains("INSERT INTO \"HostLoadSamples\"", StringComparison.Ordinal) };
            world.Interceptor = gate; world.RecreateServices(); await world.Pump.PollOnceAsync("grok-linux", CancellationToken.None);
            var held = world.Pump.ProcessOneAsync(CancellationToken.None);
            try
            {
                await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                for (var i = 1; i < 4; i++) await world.ProduceAsync(C1514HostLoadWorld.T0.AddSeconds(i * 5));
                (await world.Pump.PollOnceAsync("grok-linux", CancellationToken.None)).ShouldBeTrue();
                (await world.Pump.PollOnceAsync("grok-linux", CancellationToken.None)).ShouldBeTrue();
                (await world.Pump.PollOnceAsync("grok-linux", CancellationToken.None)).ShouldBeFalse();
                world.Pump.RefusedBatches.ShouldBe(1);
                world.Spool.ReadHistory(null, 120).Samples.Select(s => s.Source.Sequence).ShouldBe([102L, 103L, 104L, 105L]);
                await using var independent = world.Db(false); (await independent.HostLoadSamples.CountAsync(s => s.Sequence >= 102)).ShouldBe(0);
            }
            finally { gate.Release.TrySetResult(); await held; }
            await world.Pump.ProcessOneAsync(CancellationToken.None); await world.Pump.ProcessOneAsync(CancellationToken.None);
            for (var i = 0; i < 3; i++) await world.TransferAsync();
            world.Interceptor = null; world.RecreateServices(); await world.DeriveAsync();
            using var read = await world.GetAsync("/api/hosts/grok-linux/load/history?from=2026-11-06T12:00:00Z&to=2026-11-06T12:00:20Z");
            read.RootElement.GetProperty("items").EnumerateArray().Select(s => s.GetProperty("source").GetProperty("sequence").GetInt64()).ShouldBe([102L, 103L, 104L, 105L]);
            await using var fresh = world.Db(false); (await fresh.HostLoadSamples.CountAsync(s => s.Sequence >= 102)).ShouldBe(4);
        }
    }
    [Test, Timeout(60_000)]
    [Arguments("append-before-flush"), Arguments("append-after-flush")]
    [Arguments("ingest-before-enqueue"), Arguments("ingest-after-enqueue"), Arguments("ingest-before-save")]
    [Arguments("ingest-before-commit"), Arguments("ingest-after-commit")]
    [Arguments("derive-before-call"), Arguments("derive-before-save"), Arguments("derive-after-write"), Arguments("derive-after-commit")]
    public async Task RecoveryAtEveryHandoffPreservesReceipt(string cut, CancellationToken ct) => await C1514HostLoadCrashCases.RunAsync(fixture, cut, ct);
    [Test, Timeout(10_000)]
    public async Task IdentityConflictsNeverOverwriteStoredSamples()
    {
        await using var world = await fixture.WorldAsync(); await world.SeedPrefixAsync();
        var sample = await world.ProduceAsync(C1514HostLoadWorld.T0);
        await using var before = world.Db(false); var committed = (await before.HostLoadIngestCursors.SingleAsync()).Cursor;
        var page = world.History.ReadHistory(committed, 120);
        await world.TransferAsync(); world.Pump.TryEnqueue(new("grok-linux", null, page)).ShouldBeTrue();
        var replay = await world.Pump.ProcessOneAsync(CancellationToken.None); replay.Inserted.ShouldBe(0); replay.Duplicates.ShouldBe(1);
        world.Pump.TryEnqueue(new("grok-linux", replay.CommittedCursor, page with { Samples = [sample with { CpuPercent = 99, BusyCoreEquivalent = 23.76 }] })).ShouldBeTrue();
        await Should.ThrowAsync<ConflictException>(() => world.Pump.ProcessOneAsync(CancellationToken.None));
        await using var fresh = world.Db(false); var rows = await fresh.HostLoadSamples.Where(s => s.Sequence == 101).ToListAsync(); rows.Count.ShouldBe(1);
        JsonSerializer.Deserialize<HostLoadSample>(rows[0].PayloadJson, PhoneHomeFraming.Json)!.CpuPercent.ShouldBe(40d);
    }
    [Test, Timeout(10_000)]
    public async Task PrimaryEpochsDoNotDoubleCount()
    {
        await using var world = await fixture.WorldAsync(); await world.SeedPrefixAsync(); await world.ProduceAsync(C1514HostLoadWorld.T0); await world.TransferAsync();
        var runner = new Antiphon.SessionRunner.HostStatsSettings { RunnerId = "secondary", HistoryPath = Path.Combine(world.Root, "secondary"), PhysicalHostKey = "owned-host", BindingRevision = 1, SourceEpoch = world.Runner.SourceEpoch };
        var spool = new HostLoadSpool(runner.HistoryPath, runner, world.Clock); var source = spool.NextSource();
        var sample = new HostLoadSample { Source = source, SampledAt = C1514HostLoadWorld.T0, PhysicalHostKey = "owned-host", BindingRevision = 1, SourceEpoch = runner.SourceEpoch, CpuPercent = 80, HostLogicalCores = 24, BusyCoreEquivalent = 19.2 };
        world.Settings.Bindings.Add(new() { RunnerId = "secondary", PhysicalHostKey = "owned-host", SourceEpoch = runner.SourceEpoch!.Value, Primary = false });
        spool.Append(sample); var first = spool.ReadHistory(null, 120); world.Pump.TryEnqueue(new("secondary", null, first)).ShouldBeTrue();
        await world.Pump.ProcessOneAsync(CancellationToken.None); await world.DeriveAsync();
        await using (var db = world.Db(false))
        { (await db.HostLoadSamples.CountAsync(s => s.SampledAt == C1514HostLoadWorld.T0)).ShouldBe(2); (await db.HostLoadSamples.CountAsync(s => s.SampledAt == C1514HostLoadWorld.T0 && s.IsPrimary)).ShouldBe(1); }
        var transition = C1514HostLoadWorld.T0.AddSeconds(5); var epoch = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        world.Settings.Bindings[0].To = transition; world.Settings.Bindings[2].To = transition;
        world.Settings.Bindings.Add(new() { RunnerId = "secondary", PhysicalHostKey = "owned-host", SourceEpoch = epoch, From = transition });
        spool.ReadHistory(first.NextCursor, 120); spool.Append(sample with { Source = spool.NextSource(), SourceEpoch = epoch, SampledAt = transition });
        world.Pump.TryEnqueue(new("secondary", first.NextCursor, spool.ReadHistory(first.NextCursor, 120))).ShouldBeTrue();
        await world.Pump.ProcessOneAsync(CancellationToken.None); await world.DeriveAsync();
        await using var after = world.Db(false);
        (await after.HostLoadHourlyRollups.Where(r => r.PhysicalHostKey == "owned-host" && r.Metric == "cpuPercent" && r.HourUtc == C1514HostLoadWorld.T0).Select(r => r.SourceEpoch).Distinct().CountAsync()).ShouldBe(2);
        var missing = sample with { Source = spool.NextSource(), PhysicalHostKey = null, SourceEpoch = null, SampledAt = transition.AddSeconds(5) };
        using var scope = world.Services.CreateScope(); var cursor = (await after.HostLoadIngestCursors.SingleAsync(c => c.RunnerId == "secondary")).Cursor;
        await scope.ServiceProvider.GetRequiredService<HostLoadIngestService>().IngestAsync(new("secondary", cursor, new(1, missing.Source, [missing], [], null, false, true)), CancellationToken.None);
        await world.DeriveAsync(); (await after.HostLoadSamples.AsNoTracking().SingleAsync(r => r.RunnerStoreId == missing.Source.RunnerStoreId && r.Sequence == missing.Source.Sequence)).IsPrimary.ShouldBeFalse();
    }
    [Test, Timeout(10_000)]
    public async Task CountersKeepObservationTimeAndUnknowns()
    {
        await using var world = await fixture.WorldAsync(); var probe = new C1514HostLoadSqlProbe(); world.Interceptor = probe; world.RecreateServices();
        await AddTasksAsync(world, 20); probe.Commands.Clear();
        var counts = await new HostStatsAntiphonCounters(world.Services.GetRequiredService<IServiceScopeFactory>()).ReadAsync(CancellationToken.None);
        counts["grok-linux"].TasksInFlight.ShouldBe(20); probe.Commands.Count(sql => sql.Contains("FROM \"AgentTasks\"", StringComparison.Ordinal)).ShouldBe(1);
        foreach (var age in new[] { 15000, 15001 })
        {
            world.RecreateServices(); world.Services.GetRequiredService<HostLoadCounterCache>().Observe("grok-linux", new(C1514HostLoadWorld.T0, 20, counts["grok-linux"].ByStage, counts["grok-linux"].ByKind, 0, 0, 0, 0, 20, 8));
            var sample = await world.ProduceAsync(C1514HostLoadWorld.T0.AddMilliseconds(age)); await world.TransferAsync();
            await using var db = world.Db(false); var observation = JsonSerializer.Deserialize<HostLoadCounters>((await db.HostLoadSamples.SingleAsync(r => r.Sequence == sample.Source.Sequence)).CountersJson!, PhoneHomeFraming.Json)!;
            observation.TasksInFlight.ShouldBe(age == 15000 ? 20 : null); if (age == 15000) observation.CountedAt.ShouldBe(C1514HostLoadWorld.T0);
        }
        world.RecreateServices(); world.Services.GetRequiredService<HostLoadCounterCache>().Observe("grok-linux", new(world.Clock.GetUtcNow(), null, null, null, null, null, null, null, null, null));
        var failed = await world.ProduceAsync(world.Clock.GetUtcNow().AddSeconds(5)); await world.TransferAsync();
        await using var last = world.Db(false); JsonSerializer.Deserialize<HostLoadCounters>((await last.HostLoadSamples.SingleAsync(r => r.Sequence == failed.Source.Sequence)).CountersJson!, PhoneHomeFraming.Json)!.TasksInFlight.ShouldBeNull();
    }
    [Test, Timeout(10_000)]
    public async Task RawAndRollupsSurviveRetentionAndLateReplay()
    {
        await using var world = await fixture.WorldAsync(); await world.SeedPrefixAsync(); await world.ProduceAsync(C1514HostLoadWorld.T0);
        var task = await world.StartTaskAsync(C1514HostLoadWorld.T0.AddSeconds(-1)); await world.TransferAsync();
        world.Clock.Utc = C1514HostLoadWorld.T0.AddDays(8); await world.RetainAsync();
        await using (var db = world.Db(false))
        { (await db.HostLoadSamples.CountAsync()).ShouldBe(0); (await db.HostLoadHourlyRollups.CountAsync(r => r.HourUtc == C1514HostLoadWorld.T0)).ShouldBeGreaterThan(0); (await db.AgentTaskLoadWindows.SingleAsync(w => w.TaskId == task.Id)).EndedAt.ShouldBeNull(); }
        var hour = C1514HostLoadWorld.T0.AddDays(7).AddHours(12);
        await world.ProduceAsync(hour, 20); world.Clock.Utc = C1514HostLoadWorld.T0.AddDays(8); await world.TransferAsync();
        var sample = await world.ProduceAsync(hour.AddSeconds(5), 100); world.Clock.Utc = C1514HostLoadWorld.T0.AddDays(8); var page = world.History.ReadHistory(null, 120);
        await world.TransferAsync(); world.Pump.TryEnqueue(new("grok-linux", null, page)).ShouldBeTrue(); await world.Pump.ProcessOneAsync(CancellationToken.None); await world.DeriveAsync();
        await using var fresh = world.Db(false); var bucket = await fresh.HostLoadHourlyRollups.SingleAsync(r => r.HourUtc == hour && r.Metric == "cpuPercent");
        var stats = JsonSerializer.Deserialize<HostLoadMetricSummary>(bucket.StatisticsJson, PhoneHomeFraming.Json)!;
        stats.ValidCount.ShouldBe(2); stats.Sum.ShouldBe(120d); stats.Min.ShouldBe(20d); stats.Max.ShouldBe(100d); stats.P95.ShouldBe(100d);
        (await fresh.HostLoadSamples.CountAsync(r => r.BootId == sample.Source.BootId && r.Sequence == sample.Source.Sequence)).ShouldBe(1);
    }
    [Test, Timeout(10_000)]
    public async Task HistoryFailureDoesNotStopLiveSamples()
    {
        await using var world = await fixture.WorldAsync(); await world.SeedPrefixAsync(); await world.ProduceAsync(C1514HostLoadWorld.T0);
        var gate = new C1514HostLoadSqlProbe { Hold = sql => sql.Contains("INSERT INTO \"HostLoadSamples\"", StringComparison.Ordinal) };
        world.Interceptor = gate; world.RecreateServices(); await world.Pump.PollOnceAsync("grok-linux", CancellationToken.None); var held = world.Pump.ProcessOneAsync(CancellationToken.None);
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); var next = await world.ProduceAsync(C1514HostLoadWorld.T0.AddSeconds(5)); next.Source.Sequence.ShouldBe(102);
            world.Live.Latest().ShouldNotBeNull().At.ShouldBe(C1514HostLoadWorld.T0.AddSeconds(5)); world.Capture.ReadHistory(null, 120).Samples.ShouldContain(s => s.Source.Sequence == 102);
            var start = world.Pump.StartAsync(CancellationToken.None); start.IsCompleted.ShouldBeTrue(); await start;
        }
        finally { gate.Release.TrySetResult(); await held; await world.Pump.StopAsync(CancellationToken.None); }
        world.Interceptor = null; world.RecreateServices(); await world.TransferAsync(); await using var db = world.Db(false); (await db.HostLoadSamples.CountAsync(s => s.Sequence >= 101)).ShouldBe(2);
    }
    [Test, Timeout(10_000)]
    public async Task BatchesAndQueriesStayBounded()
    {
        await using var world = await fixture.WorldAsync(); var sample = await world.ProduceAsync(C1514HostLoadWorld.T0);
        var tooMany = Enumerable.Range(1, 121).Select(i => sample with { Source = sample.Source with { Sequence = i } }).ToArray();
        Should.Throw<ValidationException>(() => world.Pump.TryEnqueue(new("grok-linux", null, new(1, sample.Source, tooMany, [], null, false, true))));
        var huge = sample with { Processes = Enumerable.Range(0, 14000).Select(_ => new HostLoadProcess("session", null, 250, 1024)).ToArray() };
        Should.Throw<ValidationException>(() => world.Pump.TryEnqueue(new("grok-linux", null, new(1, sample.Source, [huge], [], null, false, true))));
        await using (var db = world.Db(false)) (await db.HostLoadSamples.CountAsync()).ShouldBe(0);
        var probe = new C1514HostLoadSqlProbe(); world.Interceptor = probe; world.RecreateServices(); await AddTasksAsync(world, 100); probe.Commands.Clear();
        await new HostStatsAntiphonCounters(world.Services.GetRequiredService<IServiceScopeFactory>()).ReadAsync(CancellationToken.None);
        probe.Commands.Count(sql => sql.Contains("FROM \"AgentTasks\"", StringComparison.Ordinal)).ShouldBe(1); await world.TransferAsync();
        using var page = await world.GetAsync("/api/hosts/grok-linux/load/history?limit=2000"); page.RootElement.GetProperty("items").GetArrayLength().ShouldBeLessThanOrEqualTo(2000);
    }
    [Test, Timeout(60_000)]
    public async Task OneRetentionPassReclaimsMoreThanOneBatch()
    {
        await using var world = await fixture.WorldAsync();
        var task = await world.StartTaskAsync(C1514HostLoadWorld.T0);
        await world.FinishTaskAsync(task.Id, C1514HostLoadWorld.T0.AddSeconds(1));
        await world.DeriveAsync();
        world.Clock.Utc = C1514HostLoadWorld.T0.AddDays(91);
        var store = Guid.NewGuid(); var boot = Guid.NewGuid(); var aged = C1514HostLoadWorld.T0;
        await using (var db = world.Db())
        {
            for (var i = 0; i < 800; i++) db.HostLoadSamples.Add(new()
            {
                RunnerStoreId = store, BootId = boot, Sequence = i + 1, RunnerId = "grok-linux", PhysicalHostKey = "owned-host",
                SampledAt = aged, IngestedAt = aged, DerivedAt = aged, IsPrimary = true, PayloadJson = "{}",
            });
            for (var i = 0; i < 3; i++) db.HostLoadSamples.Add(new()
            {
                RunnerStoreId = store, BootId = boot, Sequence = 10_000 + i, RunnerId = "grok-linux", PhysicalHostKey = "owned-host",
                SampledAt = aged, IngestedAt = aged, IsPrimary = true, PayloadJson = "{}",
            });
            for (var i = 0; i < 800; i++) db.HostLoadSampleIdentities.Add(new()
            {
                RunnerStoreId = store, BootId = boot, Sequence = i + 1, PayloadHash = "aged", IngestedAt = aged,
            });
            (await db.HostLoadObservedEvents.CountAsync()).ShouldBeGreaterThan(0);
            await db.SaveChangesAsync();
        }
        await world.RetainAsync();
        await using var after = world.Db(false);
        (await after.HostLoadSamples.CountAsync(r => r.DerivedAt == null)).ShouldBe(3);
        var expiredLeft = await after.HostLoadSamples.CountAsync(r => r.DerivedAt != null);
        var identitiesLeft = await after.HostLoadSampleIdentities.CountAsync();
        var observedLeft = await after.HostLoadObservedEvents.CountAsync();
        (expiredLeft + identitiesLeft + observedLeft).ShouldBe(0, "expired=" + expiredLeft + " identities=" + identitiesLeft + " observed=" + observedLeft);
    }
    [Test, Timeout(10_000)]
    public async Task FullSeatsRecommendNothingWhenTaskRowsAreEmpty()
    {
        await using var world = await fixture.WorldAsync();
        await using var db = world.Db();
        var directory = new FullSeatDirectory(world.Clock.GetUtcNow());
        var budgets = new HostBudgetService(db, directory, Options.Create(new DelegationSettings { MaxConcurrentTasks = 8 }), world.Clock);
        var reader = new HostLoadCapacityReader(db, budgets, directory, world.Clock);
        var capacity = await reader.ReadAsync("grok-linux", null, null, CancellationToken.None);
        capacity.RunnerSeatsRemaining.ShouldBe(0);
        capacity.HostRemaining.ShouldBe(3);
        var now = world.Clock.GetUtcNow();
        var advice = new HostLoadRecommendation().Evaluate(
            new HostLoadPressureInput("owned-host", "owned-host", now, 10, 10, 10), capacity, [], now, false, false);
        advice.Recommendation.RecommendedAdditionalTasks.ShouldBe(0);
    }
    private static async Task AddTasksAsync(C1514HostLoadWorld world, int count)
    {
        await using var db = world.Db();
        for (var i = 0; i < count; i++) db.AgentTasks.Add(new() { Id = Guid.NewGuid(), RootTaskId = Guid.NewGuid(), Title = "owned count", Goal = "owned", Role = AgentTaskRole.Code,
            AgentKind = AgentKind.Codex, RunnerId = "grok-linux", Status = AgentTaskStatus.Working, CreatedAt = C1514HostLoadWorld.T0.UtcDateTime });
        await db.SaveChangesAsync();
    }
    private sealed class FullSeatDirectory(DateTimeOffset observed) : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local => null!;
        public ISessionRunnerClient Resolve(string? runnerId) => null!;
        public IReadOnlyList<string> KnownRunnerIds => ["grok-linux"];
        public Guid? GetLiveStoreId(string? runnerId) => null;
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) => Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) => Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Local.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) => Task.FromResult<RunnerInventory>(new RunnerInventory.Available(
        [
            new SessionRunnerSessionDto(Guid.NewGuid(), 11, observed.UtcDateTime, "Running", null, AgentExitReason.Unknown, 0),
            new SessionRunnerSessionDto(Guid.NewGuid(), 12, observed.UtcDateTime, "Starting", null, AgentExitReason.Unknown, 0),
            new SessionRunnerSessionDto(Guid.NewGuid(), 13, observed.UtcDateTime, "AwaitingInput", null, AgentExitReason.Unknown, 0),
        ]));
        public Task<RunnerDescriptor?> DescribeAsync(string? runnerId, CancellationToken ct) => Task.FromResult<RunnerDescriptor?>(
            new("grok-linux", "grok-linux", "linux", observed, true, true, false, 3, null));
        public int? DeclaredCapacity(string runnerId) => 3;
    }
}
