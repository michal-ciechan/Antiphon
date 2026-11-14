using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.HostLoad;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;
namespace Antiphon.Tests.Application;
// Provisional, two Linux completions, not a p95 (CARD-1514 R5).
// AttemptsAndMovesKeepSeparateWindows was killed at the 10s class cap, then finished in 16.3s
// (schema-cache 13.5s inside that run) and 26.2s (schema-cache 4.3s). The fixture poll allows 15s,
// which is longer than that cap, so a slow poll died in the TUnit timeout handler.
// 2x the slower completion is 52s. 60s matches the SeedPrefix methods on C1514HostLoadHistoryTests.
// The 26.2s completion was observed under a 45s ceiling; assertions are unchanged.
[Category("Integration"), C1514ProgressBudget(60_000), NotInParallel("C1514HostLoadServer")]
[TUnit.Core.Executors.TestExecutor<C1514HostLoadProgressExecutor>]
[ClassDataSource<C1514HostLoadFixture>(Shared = SharedType.PerTestSession)]
public class C1514TaskLoadWindowTests(C1514HostLoadFixture fixture)
{
    [Before(Class)]
    public static Task WarmDatabase() => TestDbFixture.Lifecycle.EnsureReadyAsync();
    [Test]
    public async Task AttemptsAndMovesKeepSeparateWindows()
    {
        await using var world = await fixture.WorldAsync(); await world.SeedPrefixAsync();
        var first = await world.ProduceAsync(C1514HostLoadWorld.T0); var task = await world.StartTaskAsync(C1514HostLoadWorld.T0.AddSeconds(-1)); await world.TransferAsync();
        await world.FinishTaskAsync(task.Id, C1514HostLoadWorld.T0.AddSeconds(1)); world.Clock.Utc = C1514HostLoadWorld.T0.AddSeconds(2);
        await world.RetryThroughServiceAsync(task.Id); await world.DeriveAsync();
        var epochB = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        world.Settings.Bindings.Add(new() { RunnerId = "host-b-runner", PhysicalHostKey = "host-b", SourceEpoch = epochB });
        var settingsB = new Antiphon.SessionRunner.HostStatsSettings { RunnerId = "host-b-runner", HistoryPath = Path.Combine(world.Root, "host-b"), PhysicalHostKey = "host-b", SourceEpoch = epochB, BindingRevision = 1 };
        var spoolB = new HostLoadSpool(settingsB.HistoryPath, settingsB, world.Clock);
        var second = first with { Source = spoolB.NextSource(), PhysicalHostKey = "host-b", SourceEpoch = epochB, SampledAt = C1514HostLoadWorld.T0.AddSeconds(11), CpuPercent = 80, BusyCoreEquivalent = 19.2 };
        spoolB.Append(second); world.Pump.TryEnqueue(new("host-b-runner", null, spoolB.ReadHistory(null, 120))).ShouldBeTrue(); await world.ProcessOneAsync(world.Token);
        await world.StartTaskAsync(C1514HostLoadWorld.T0.AddSeconds(10), "host-b-runner", 2, task.Id); await world.DeriveAsync();
        await world.Capture.StopAsync(world.Token);
        var bootC = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var spoolC = new HostLoadSpool(world.Runner.HistoryPath!, world.Runner, world.Clock, bootC);
        await using var db = world.Db(false); var cursor = (await db.HostLoadIngestCursors.SingleAsync(c => c.RunnerId == "grok-linux")).Cursor;
        spoolC.ReadHistory(cursor, 120); var third = first with { Source = spoolC.NextSource(), SampledAt = C1514HostLoadWorld.T0.AddSeconds(21), CpuPercent = 60, BusyCoreEquivalent = 14.4 };
        spoolC.Append(third); world.Pump.TryEnqueue(new("grok-linux", cursor, spoolC.ReadHistory(cursor, 120))).ShouldBeTrue(); await world.ProcessOneAsync(world.Token);
        await world.StartTaskAsync(C1514HostLoadWorld.T0.AddSeconds(20), "grok-linux", 2, task.Id); await world.DeriveAsync();
        var rows = await db.AgentTaskLoadWindows.AsNoTracking().Where(w => w.TaskId == task.Id).OrderBy(w => w.StartedAt).ToListAsync();
        rows.Count.ShouldBe(3); rows.Select(w => w.Attempt).ShouldBe([1, 2, 2]); rows.Select(w => w.PhysicalHostKey).ShouldBe(["owned-host", "host-b", "owned-host"]);
        rows.Select(w => w.RunnerBootId).ShouldBe([first.Source.BootId, second.Source.BootId, bootC]);
        using var api = await world.GetAsync("/api/agent-tasks/" + task.Id + "/load?attempt=2"); api.RootElement.GetProperty("windows").GetArrayLength().ShouldBe(2);
    }
    [Test]
    public async Task BoundarySamplesRespectFifteenSeconds()
    {
        await using var world = await fixture.WorldAsync(); await world.SeedPrefixAsync();
        var earlier = await world.ProduceAsync(C1514HostLoadWorld.T0.AddSeconds(-15)); await world.TransferAsync();
        await world.ProduceAsync(C1514HostLoadWorld.T0.AddSeconds(15)); await world.TransferAsync();
        var task = await world.StartTaskAsync(C1514HostLoadWorld.T0); await world.DeriveAsync();
        using (var api = await world.GetAsync("/api/agent-tasks/" + task.Id + "/load"))
        { var start = api.RootElement.GetProperty("windows")[0].GetProperty("start"); start.GetProperty("eventAt").GetDateTimeOffset().ShouldBe(C1514HostLoadWorld.T0);
            start.GetProperty("sampleIdentity").GetProperty("sequence").GetInt64().ShouldBe(earlier.Source.Sequence); start.GetProperty("ageSeconds").GetDouble().ShouldBe(15); }
        await world.ProduceAsync(C1514HostLoadWorld.T0.AddSeconds(1)); await world.TransferAsync();
        using (var frozen = await world.GetAsync("/api/agent-tasks/" + task.Id + "/load")) frozen.RootElement.GetProperty("windows")[0].GetProperty("start").GetProperty("sampleIdentity").GetProperty("sequence").GetInt64().ShouldBe(earlier.Source.Sequence);
        var boundary = await world.StartTaskAsync(C1514HostLoadWorld.T0.AddSeconds(30));
        var stale = await world.StartTaskAsync(C1514HostLoadWorld.T0.AddMilliseconds(30001)); world.Clock.Utc = C1514HostLoadWorld.T0.AddSeconds(31); await world.DeriveAsync();
        using var valid = await world.GetAsync("/api/agent-tasks/" + boundary.Id + "/load"); valid.RootElement.GetProperty("windows")[0].GetProperty("start").GetProperty("state").GetString().ShouldBe("known");
        using var missing = await world.GetAsync("/api/agent-tasks/" + stale.Id + "/load"); missing.RootElement.GetProperty("windows")[0].GetProperty("start").GetProperty("state").GetString().ShouldBe("missing");
    }
    [Test]
    public async Task ShortAndOverlappingTasksKeepAmbientMeaning()
    {
        await using var world = await fixture.WorldAsync(); await world.SeedPrefixAsync(); var sample = await world.ProduceAsync(C1514HostLoadWorld.T0); await world.TransferAsync();
        var a = await world.StartTaskAsync(C1514HostLoadWorld.T0.AddSeconds(-10)); var b = await world.StartTaskAsync(C1514HostLoadWorld.T0.AddSeconds(-5));
        var shortTask = await world.StartTaskAsync(C1514HostLoadWorld.T0.AddSeconds(1));
        await world.FinishTaskAsync(a.Id, C1514HostLoadWorld.T0.AddSeconds(1)); await world.FinishTaskAsync(b.Id, C1514HostLoadWorld.T0.AddSeconds(1));
        await world.FinishTaskAsync(shortTask.Id, C1514HostLoadWorld.T0.AddSeconds(2)); world.Clock.Utc = C1514HostLoadWorld.T0.AddSeconds(3); await world.DeriveAsync();
        using var shortApi = await world.GetAsync("/api/agent-tasks/" + shortTask.Id + "/load"); var shortWindow = shortApi.RootElement.GetProperty("windows")[0];
        shortWindow.GetProperty("during").GetProperty("cpuPercent").GetProperty("avg").ValueKind.ShouldBe(JsonValueKind.Null);
        shortWindow.GetProperty("start").GetProperty("sampleIdentity").GetProperty("sequence").GetInt64().ShouldBe(sample.Source.Sequence);
        shortWindow.GetProperty("end").GetProperty("sampleIdentity").GetProperty("sequence").GetInt64().ShouldBe(sample.Source.Sequence);
        foreach (var id in new[] { a.Id, b.Id }) { using var api = await world.GetAsync("/api/agent-tasks/" + id + "/load"); var window = api.RootElement.GetProperty("windows")[0]; window.GetProperty("attribution").GetString().ShouldBe("ambientHost"); window.GetProperty("overlapCount").GetInt32().ShouldBe(2); }
    }
    [Test]
    public async Task RestartReconcilesMissedEventsWithoutClosingOpenTask()
    {
        await using var world = await fixture.WorldAsync(); await world.SeedPrefixAsync(); await world.ProduceAsync(C1514HostLoadWorld.T0);
        var task = await world.StartTaskAsync(C1514HostLoadWorld.T0.AddSeconds(-1)); await world.TransferAsync();
        var open = await world.StartTaskAsync(C1514HostLoadWorld.T0.AddSeconds(1)); await world.FinishTaskAsync(task.Id, C1514HostLoadWorld.T0.AddSeconds(2));
        world.RecreateServices(); await world.DeriveAsync(); world.RecreateServices(); await world.DeriveAsync();
        await using var db = world.Db(false); (await db.AgentTaskLoadWindows.SingleAsync(w => w.TaskId == task.Id)).EndedAt.ShouldBe(C1514HostLoadWorld.T0.AddSeconds(2));
        (await db.AgentTaskLoadWindows.SingleAsync(w => w.TaskId == open.Id)).EndedAt.ShouldBeNull();
        var historical = await world.StartTaskAsync(C1514HostLoadWorld.T0.AddSeconds(3));
        await db.AgentTaskEvents.Where(e => e.AgentTaskId == historical.Id).ExecuteUpdateAsync(s => s.SetProperty(e => e.LoadAttempt, (int?)null));
        world.RecreateServices(); await world.DeriveAsync();
        using var api = await world.GetAsync("/api/agent-tasks/" + historical.Id + "/load"); api.RootElement.GetProperty("complete").GetBoolean().ShouldBeFalse(); api.RootElement.GetProperty("gaps").GetArrayLength().ShouldBeGreaterThan(0);
    }
    [Test]
    public async Task LongTasksKeepStatisticsAfterRawExpiry()
    {
        await using var world = await fixture.WorldAsync(); await world.SeedPrefixAsync(); var source = await world.ProduceAsync(C1514HostLoadWorld.T0);
        var task = await world.StartTaskAsync(C1514HostLoadWorld.T0.AddSeconds(-1)); await world.TransferAsync();
        world.Clock.Utc = C1514HostLoadWorld.T0.AddDays(8); await world.RetainAsync();
        world.Clock.Utc = C1514HostLoadWorld.T0.AddDays(91); await world.RetainAsync();
        using (var api = await world.GetAsync("/api/agent-tasks/" + task.Id + "/load"))
        { var window = api.RootElement.GetProperty("windows")[0]; window.GetProperty("state").GetString().ShouldBe("open"); window.GetProperty("start").GetProperty("sampleIdentity").GetProperty("sequence").GetInt64().ShouldBe(source.Source.Sequence);
            var cpu = window.GetProperty("during").GetProperty("cpuPercent"); cpu.GetProperty("validCount").GetInt64().ShouldBe(1); cpu.GetProperty("sum").GetDouble().ShouldBe(40); cpu.GetProperty("min").GetDouble().ShouldBe(40); cpu.GetProperty("max").GetDouble().ShouldBe(40); cpu.GetProperty("p95Approximate").GetBoolean().ShouldBeTrue(); cpu.GetProperty("p95ErrorBound").GetDouble().ShouldBeGreaterThan(0); }
        await world.FinishTaskAsync(task.Id, world.Clock.GetUtcNow()); await world.DeriveAsync(); world.Clock.Utc = world.Clock.Utc.AddDays(89); await world.RetainAsync();
        await using var db = world.Db(false); (await db.AgentTaskLoadWindows.CountAsync(w => w.TaskId == task.Id)).ShouldBe(1);
        world.Clock.Utc = world.Clock.Utc.AddDays(2); await world.RetainAsync(); (await db.AgentTaskLoadWindows.AsNoTracking().CountAsync(w => w.TaskId == task.Id)).ShouldBe(0);
    }
    [Test]
    public async Task ReusedSessionOpensAtAcceptance()
    {
        await using var world = await fixture.WorldAsync();
        var accepted = C1514HostLoadWorld.T0;
        await using var db = world.Db();
        var session = new AgentSession
        {
            Id = Guid.NewGuid(), DefinitionName = "owned", AgentKind = AgentKind.Codex, Status = SessionStatus.Running, Cwd = world.Root,
            CreatedAt = accepted.AddHours(-2).UtcDateTime, StartedAt = accepted.AddHours(-2).UtcDateTime, LastSeenAt = accepted.UtcDateTime,
        };
        var task = new AgentTask
        {
            Id = Guid.NewGuid(), Title = "reused seat", Goal = "owned", Status = AgentTaskStatus.Working,
            CreatedAt = accepted.AddSeconds(-20).UtcDateTime, Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, AgentKind = AgentKind.Codex,
            Workspace = WorkspaceMode.Shared, WorkingDirectory = world.Root, TokenHash = AgentTaskService.HashToken("owned-task-token"),
            RunnerId = "grok-linux", Attempt = 1, DispatchedAt = accepted.UtcDateTime, AgentSessionId = session.Id,
        };
        task.RootTaskId = task.Id;
        db.AgentSessions.Add(session); db.AgentTasks.Add(task);
        db.AgentTaskEvents.Add(new() { Id = Guid.NewGuid(), AgentTaskId = task.Id, AgentSessionId = session.Id, Type = AgentTaskEventType.Dispatched, At = accepted.AddSeconds(1).UtcDateTime });
        await db.SaveChangesAsync();
        await world.DeriveAsync();
        using var api = await world.GetAsync("/api/agent-tasks/" + task.Id + "/load");
        var windows = api.RootElement.GetProperty("windows");
        windows.GetArrayLength().ShouldBe(1);
        windows[0].GetProperty("startedAt").GetDateTimeOffset().ShouldBe(accepted);
    }
    [Test]
    public async Task BackwardsTerminalClosesWithoutLeavingTheWindowOpen()
    {
        await using var world = await fixture.WorldAsync();
        var start = C1514HostLoadWorld.T0;
        var task = await world.StartTaskAsync(start);
        await world.DeriveAsync();
        await using var db = world.Db();
        (await db.AgentTaskLoadWindows.SingleAsync(w => w.TaskId == task.Id)).StartedAt.ShouldBe(start);
        var tracked = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
        tracked.Status = AgentTaskStatus.Succeeded; tracked.CompletedAt = start.AddSeconds(-1).UtcDateTime;
        db.AgentTaskEvents.Add(new() { Id = Guid.NewGuid(), AgentTaskId = task.Id, Type = AgentTaskEventType.Completed, At = start.AddSeconds(-1).UtcDateTime });
        await db.SaveChangesAsync();
        await world.DeriveAsync();
        await using var after = world.Db(false);
        var closed = await after.AgentTaskLoadWindows.SingleAsync(w => w.TaskId == task.Id);
        closed.EndedAt.ShouldNotBeNull();
        closed.Complete.ShouldBeFalse();
    }
    [Test]
    public async Task KnownBoundariesWithMissingInteriorAreIncomplete()
    {
        await using var world = await fixture.WorldAsync();
        var start = C1514HostLoadWorld.T0; var end = start.AddMinutes(15);
        await world.ProduceAsync(start); await world.TransferAsync();
        var task = await world.StartTaskAsync(start);
        await world.ProduceAsync(end); await world.TransferAsync();
        await world.FinishTaskAsync(task.Id, end); world.Clock.Utc = end; await world.DeriveAsync();
        using var api = await world.GetAsync("/api/agent-tasks/" + task.Id + "/load");
        var window = api.RootElement.GetProperty("windows")[0];
        window.GetProperty("start").GetProperty("state").GetString().ShouldBe("known");
        window.GetProperty("end").GetProperty("state").GetString().ShouldBe("known");
        var cpu = window.GetProperty("during").GetProperty("cpuPercent");
        var expected = HostLoadAggregation.ExpectedCount(start.AddTicks(1), end);
        expected.ShouldBeGreaterThan(0);
        cpu.GetProperty("validCount").GetInt64().ShouldBe(0);
        cpu.GetProperty("expectedCount").GetInt64().ShouldBe(expected);
        cpu.GetProperty("complete").GetBoolean().ShouldBeFalse();
        api.RootElement.GetProperty("complete").GetBoolean().ShouldBeFalse();
        api.RootElement.GetProperty("gaps").GetArrayLength().ShouldBeGreaterThan(0);
    }
}
