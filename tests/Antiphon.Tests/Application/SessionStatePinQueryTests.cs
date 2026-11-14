using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[NotInParallel]
[ClassDataSource<SessionStateScaleFixture>(Shared = SharedType.PerTestSession)]
public class SessionStatePinQueryTests(SessionStateScaleFixture scale)
{
    [Test]
    public async Task Frozen_seed_matches_randomized_histories()
    {
        // S1's oracle is independently frozen SQL, never the loader's private query.
        var sql = SessionStateSeedBaseline.Sql;
        var capture = new SessionStateSeedCapture();
        await using var f = await SessionStateTestFixture.CreateAsync(fault: capture);
        await using var db = f.Db();
        var random = new Random(1915);
        var ids = Enumerable.Range(0, 128).Select(_ => Guid.NewGuid()).ToArray();
        db.AgentSessions.AddRange(ids.Select(id => new AgentSession { Id = id, DefinitionName = "c1915", Cwd = "/synthetic", Status = SessionStatus.Stopped }));
        var kinds = new[] { "AssistantText", "TurnEnd", "TurnTitle", "UserPrompt", "CompactBoundary", "SessionRestartBoundary", "QueueEnqueue" };
        var texts = new string?[] { null, "hello", "(manual)", "(auto)", "[Request interrupted by user]", "<command-name>x", "<local-command-stdout>x", "This session is being continued from a previous conversation" };
        foreach (var id in ids.Skip(1))
        foreach (var n in Enumerable.Range(0, random.Next(1, 25)))
            db.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = id,
                Sequence = n == 0 ? long.MinValue : n * 13 - 31, Kind = kinds[random.Next(kinds.Length)], Text = texts[random.Next(texts.Length)],
                Timestamp = random.Next(4) == 0 ? null : new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(random.Next(5)),
                CreatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc) });
        await db.SaveChangesAsync();
        var requested = ids.Append(Guid.NewGuid()).ToArray();
        capture.Commands.Clear();
        var actual = await f.Services.GetRequiredService<ISessionStateLoader>().LoadAsync(requested, default);
        actual[ids[0]].Count.ShouldBe(0);
        actual[requested[^1]].Readiness.ShouldBe(SessionStateReadiness.Missing);
        await db.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        (await RowsAsync(connection, capture.Commands.Single())).ShouldBe(await RowsAsync(connection,
            new CapturedTranscriptCommand(sql, capture.Commands.Single().Parameters)));
    }

    [Test]
    public async Task Pin_membership_preserves_all_reasons()
    {
        var capture = new SessionStateSeedCapture();
        await using var f = await SessionStateTestFixture.CreateAsync(fault: capture);
        await using var db = f.Db();
        await db.AgentSessions.ExecuteDeleteAsync();
        var sessions = new List<AgentSession>();
        var expected = new HashSet<Guid>();
        Guid Session(bool pinned, SessionStatus status = SessionStatus.Stopped)
        {
            var s = new AgentSession { Id = Guid.NewGuid(), Status = status, DefinitionName = "c1887", Cwd = "/synthetic", CreatedAt = DateTime.UtcNow };
            sessions.Add(s);
            if (pinned) expected.Add(s.Id);
            return s.Id;
        }
        foreach (var status in Enum.GetValues<SessionStatus>())
            Session(status is SessionStatus.Starting or SessionStatus.Running or SessionStatus.Stopping, status);
        foreach (var status in Enum.GetValues<AgentTaskStatus>())
        {
            var open = status is AgentTaskStatus.Queued or AgentTaskStatus.Dispatched or AgentTaskStatus.Working or AgentTaskStatus.Blocked;
            var child = Session(open); var parent = Session(open); var both = Session(open);
            db.AgentTasks.AddRange(new AgentTask { Id = Guid.NewGuid(), Status = status, AgentSessionId = child },
                new AgentTask { Id = Guid.NewGuid(), Status = status, ParentSessionId = parent },
                new AgentTask { Id = Guid.NewGuid(), Status = status, AgentSessionId = both, ParentSessionId = both },
                new AgentTask { Id = Guid.NewGuid(), Status = status });
        }
        var missing = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask { Id = Guid.NewGuid(), ParentSessionId = missing });
        foreach (var status in Enum.GetValues<QueuedMessageStatus>())
        foreach (var origin in Enum.GetValues<QueuedMessageOrigin>())
        foreach (var verdict in new DeliveryVerdict?[] { null, DeliveryVerdict.Delivered, DeliveryVerdict.LateConfirmed })
        foreach (var settled in new[] { false, true })
        {
            var pinned = status == QueuedMessageStatus.Pending || status == QueuedMessageStatus.Sent &&
                (verdict is null || origin == QueuedMessageOrigin.Channel && !settled);
            db.SessionQueuedMessages.Add(new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = Session(pinned),
                Status = status, Origin = origin, DeliveryVerdict = verdict, ChannelReplySettledAt = settled ? DateTime.UtcNow : null });
        }
        var owner = new AgentTask { Id = Guid.NewGuid(), Status = AgentTaskStatus.Succeeded };
        db.AgentTasks.Add(owner);
        foreach (var state in Enum.GetValues<LandNotificationState>())
        foreach (var completed in new[] { false, true })
        {
            var ev = new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = owner.Id };
            var dispatch = new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = owner.Id };
            db.AgentTaskEvents.AddRange(ev, dispatch);
            db.AgentTaskLandNotifications.Add(new AgentTaskLandNotification { Id = Guid.NewGuid(), TaskId = owner.Id,
                SourceEventId = ev.Id, ParentSessionId = Session(!completed && state != LandNotificationState.NotRequired),
                State = state, ConfirmedAt = completed ? DateTime.UtcNow : null });
            db.AgentTaskDispatchWarningIntents.Add(new AgentTaskDispatchWarningIntent { Id = Guid.NewGuid(), TaskId = owner.Id,
                DispatchEventId = dispatch.Id, NotificationId = Guid.NewGuid(), WarningKey = "synthetic",
                ParentSessionId = Session(!completed && state != LandNotificationState.NotRequired), InitialState = state,
                MaterializedAt = completed ? DateTime.UtcNow : null });
        }
        db.AgentSessions.AddRange(sessions);
        await db.SaveChangesAsync();
        // Timestamp ties, NULLs and reverse producer-time order exercise the unchanged seed projection.
        db.TranscriptEntries.AddRange(Enumerable.Range(1, 1500).Select(n => new TranscriptEntry {
            Id = Guid.NewGuid(), AgentSessionId = sessions[0].Id, Sequence = n,
            Kind = n % 10 == 0 ? "TurnEnd" : n % 3 == 0 ? "UserPrompt" : "AssistantText",
            Timestamp = n % 7 == 0 ? null : new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(n % 5),
            CreatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc) }));
        await db.SaveChangesAsync();
        var ids = sessions.Select(s => s.Id).Append(missing).ToArray();
        ids.Length.ShouldBeLessThanOrEqualTo(512);
        var loader = f.Services.GetRequiredService<ISessionStateLoader>();
        capture.Commands.Clear();
        var seeds = await loader.LoadAsync(ids, default);
        capture.Commands.Count.ShouldBe(1, "seed is one command including pin membership");
        var seedCommand = capture.Commands.Single();
        capture.Commands.Clear();
        var pins = await loader.LoadPinnedIdsAsync(default);
        capture.Commands.Count.ShouldBe(1, "pins is one command");
        pins.Order().ShouldBe(expected.Order());
        seeds.Where(p => p.Value.Pinned).Select(p => p.Key).Order().ShouldBe(expected.Order());
        seeds[missing].Readiness.ShouldBe(SessionStateReadiness.Missing);
        seeds[missing].Pinned.ShouldBeFalse();
        seeds[sessions[0].Id].Count.ShouldBe(1500);
        seeds[sessions[0].Id].LastSequence.ShouldBe(1500);
        await using var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        (await RowsAsync(connection, seedCommand)).ShouldBe(await RowsAsync(connection,
            new(SessionStatePinBaseline.Sql, seedCommand.Parameters)));
        (await RowsAsync(connection, capture.Commands.Single())).ShouldBe(await RowsAsync(connection,
            new(SessionStatePinBaseline.PinsSql, [])));
        // A subsequent statement must see task status changes; no process membership cache.
        await db.AgentTasks.Where(t => t.Status == AgentTaskStatus.Blocked).ExecuteUpdateAsync(u => u.SetProperty(t => t.Status, AgentTaskStatus.Succeeded));
        var blockedIds = sessions.Skip(6 + 3 * 3).Take(3).Select(s => s.Id).ToArray();
        (await loader.LoadPinnedIdsAsync(default)).ShouldNotContain(id => blockedIds.Contains(id));
        (await loader.LoadAsync(blockedIds, default)).Values.ShouldAllBe(s => !s.Pinned);
    }

    [Test]
    public async Task Scale_plans_enumerate_open_tasks_once()
    {
        var (f, ids) = await scale.GetAsync();
        var capture = scale.Capture;
        await using var db = f.Db();
        await db.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using (var settings = new NpgsqlCommand("SET jit = off; SET statement_timeout = '60s';", connection))
            await settings.ExecuteNonQueryAsync();
        var loader = f.Services.GetRequiredService<ISessionStateLoader>();
        var comparisons = new List<(string Name, JsonElement Before, JsonElement After)>();
        foreach (var count in new[] { 1, 64, 512 })
        {
            capture.Commands.Clear();
            var snapshots = await loader.LoadAsync(ids.Take(count).ToArray(), default);
            snapshots[ids[0]].Count.ShouldBe(75000);
            if (count == 512)
            {
                snapshots[ids[511]].Count.ShouldBe(0);
                snapshots[ids[32]].Count.ShouldBe(1500);
                snapshots.Values.Sum(s => s.Count).ShouldBe(3118500);
            }
            capture.Commands.Count.ShouldBe(1);
            var production = capture.Commands.Single();
            var baseline = new CapturedTranscriptCommand(SessionStatePinBaseline.Sql, production.Parameters);
            Record($"sql-seed-{count}", new { before = baseline.Evidence(), after = production.Evidence() });
            (await RowsAsync(connection, production)).ShouldBe(await RowsAsync(connection, baseline));
            (await RowsAsync(connection, production)).ShouldBe(await RowsAsync(connection, new(SessionStateSeedBaseline.Sql, production.Parameters)));
            comparisons.Add(($"seed-{count}", await ExplainAsync(connection, baseline), await ExplainAsync(connection, production)));
        }
        capture.Commands.Clear();
        (await loader.LoadPinnedIdsAsync(default)).Order().ShouldBe(ids.Take(32).Order());
        capture.Commands.Count.ShouldBe(1);
        var pins = capture.Commands.Single();
        Record("sql-pins", new { before = SessionStatePinBaseline.PinsSql, after = pins.Evidence() });
        (await RowsAsync(connection, pins)).ShouldBe(await RowsAsync(connection, new(SessionStatePinBaseline.PinsSql, [])));
        comparisons.Add(("pins", await ExplainAsync(connection, new(SessionStatePinBaseline.PinsSql, [])), await ExplainAsync(connection, pins)));
        foreach (var (name, before, after) in comparisons) Record(name, new { before, after });
        foreach (var (name, _, after) in comparisons)
        {
            var taskScans = TranscriptPlanAssertions.Nodes(after).Where(n => TranscriptPlanAssertions.Property(n, "Relation Name") == "AgentTasks").ToArray();
            taskScans.ShouldHaveSingleItem(name);
            taskScans.ShouldAllBe(n => n.GetProperty("Actual Loops").GetInt64() <= 1, "open tasks must not be rescanned per session: " + name);
            TranscriptPlanAssertions.Nodes(after).ShouldContain(n => TranscriptPlanAssertions.Property(n, "Index Name") == "IX_AgentTasks_Status", name);
            TranscriptPlanAssertions.NoTranscriptSequentialScan(after);
        }
        var fleet = comparisons.Single(p => p.Name == "pins");
        var newRoot = fleet.After[0].GetProperty("Plan");
        newRoot.GetProperty("Total Cost").GetDouble().ShouldBeLessThan(100000);
        Blocks(fleet.Before).ShouldBeGreaterThan(100 * Blocks(fleet.After), "fleet pins should remove repeated task-buffer work");
    }

    private static long Blocks(JsonElement plan)
    {
        var root = plan[0].GetProperty("Plan");
        return root.GetProperty("Shared Hit Blocks").GetInt64() + root.GetProperty("Shared Read Blocks").GetInt64();
    }

    private static async Task<string> RowsAsync(NpgsqlConnection connection, CapturedTranscriptCommand sql)
    {
        await using var command = new NpgsqlCommand("SELECT COALESCE(jsonb_agg(to_jsonb(r) ORDER BY to_jsonb(r)::text), '[]'::jsonb)::text FROM (" + sql.Sql + ") r", connection) { CommandTimeout = 60 };
        command.Parameters.AddRange(sql.Parameters.Select(p => p.Clone()).ToArray());
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<JsonElement> ExplainAsync(NpgsqlConnection connection, CapturedTranscriptCommand sql)
    {
        Record("load-" + Guid.NewGuid().ToString("N"), new { utc = DateTime.UtcNow,
            load = File.Exists("/proc/loadavg") ? File.ReadAllText("/proc/loadavg") : "unknown" });
        await using var command = new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS, SETTINGS, FORMAT JSON, TIMING OFF) " + sql.Sql, connection) { CommandTimeout = 60 };
        command.Parameters.AddRange(sql.Parameters.Select(p => p.Clone()).ToArray());
        // Observe our owned backend while EXPLAIN is active; this reads no production backend.
        await using var monitor = new NpgsqlConnection(TestDbFixture.ConnectionString);
        await monitor.OpenAsync();
        var running = command.ExecuteScalarAsync();
        await using var activity = new NpgsqlCommand("SELECT jsonb_build_object('activity',(SELECT row_to_json(a) FROM (SELECT wait_event_type,wait_event FROM pg_stat_activity WHERE pid=@pid) a),'locks',(SELECT jsonb_agg(row_to_json(l)) FROM (SELECT locktype,mode,granted FROM pg_locks WHERE pid=@pid) l))::text", monitor);
        activity.Parameters.AddWithValue("pid", connection.ProcessID);
        var observation = await activity.ExecuteScalarAsync();
        using var json = JsonDocument.Parse((string)(await running)!);
        Record("wait-" + Guid.NewGuid().ToString("N"), new { observation });
        return json.RootElement.Clone();
    }

    private static async Task<string> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static void Record(string name, object value)
    {
        var root = Path.Combine(Environment.GetEnvironmentVariable("ANTIPHON_C1887_EVIDENCE") ?? Path.GetTempPath(), "c1887-pin-plans");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name + ".json"), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }

}
