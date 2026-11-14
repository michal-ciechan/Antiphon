using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Antiphon.Tests.TestHelpers;

/// <summary>One lazy, immutable 3,118,500-row baseline per TUnit session. Mutators use a transaction or owned clone.</summary>
public sealed class SessionStateScaleFixture : IAsyncDisposable
{
    private readonly Lazy<Task<(SessionStateTestFixture World, Guid[] Ids)>> _baseline;
    internal SessionStateSeedCapture Capture { get; } = new();
    public SessionStateScaleFixture() => _baseline = new(CreateAsync);
    internal Task<(SessionStateTestFixture World, Guid[] Ids)> GetAsync() => _baseline.Value;

    private async Task<(SessionStateTestFixture World, Guid[] Ids)> CreateAsync()
    {
        var f = await SessionStateTestFixture.CreateAsync(fault: Capture);
        try
        {
        await using var db = f.Db();
        await db.AgentSessions.ExecuteDeleteAsync();
        var ids = Enumerable.Range(0, 512).Select(_ => Guid.NewGuid()).ToArray();
        db.AgentSessions.AddRange(ids.Select(id => new AgentSession { Id = id, DefinitionName = "c1887-scale", Status = SessionStatus.Stopped, Cwd = "/synthetic", CreatedAt = DateTime.UtcNow }));
        // Populate wide task defaults once through EF; bulk-copy that owned prototype.
        // Tracking 27,695 wide entities dominated setup in the first two receipts.
        var prototype = new AgentTask { Id = Guid.NewGuid(), Status = AgentTaskStatus.Succeeded, Goal = "synthetic task history" };
        db.AgentTasks.Add(prototype);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await db.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var timer = Stopwatch.StartNew();
        await using (var tasks = new NpgsqlCommand("""
            WITH prototype AS MATERIALIZED (SELECT to_jsonb(t) AS payload FROM "AgentTasks" t WHERE "Id" = @prototype),
            tasks AS MATERIALIZED (
            SELECT jsonb_populate_record(NULL::"AgentTasks", payload || jsonb_build_object(
                'Id', gen_random_uuid(), 'ConcurrencyToken', gen_random_uuid(),
                'Status', CASE WHEN n < 256 THEN n % 4 ELSE 4 END,
                'AgentSessionId', CASE WHEN n % 3 = 0 THEN NULL ELSE (@ids)[n % 32 + 1] END,
                'ParentSessionId', CASE WHEN n % 3 = 1 THEN NULL ELSE (@ids)[n % 32 + 1] END)) AS item
            FROM prototype CROSS JOIN generate_series(0, 27694) n)
            INSERT INTO "AgentTasks" SELECT (item).* FROM tasks;
            DELETE FROM "AgentTasks" WHERE "Id" = @prototype;
            """, connection) { CommandTimeout = 60 })
        {
            tasks.Parameters.AddWithValue("prototype", prototype.Id);
            tasks.Parameters.AddWithValue("ids", ids);
            await tasks.ExecuteNonQueryAsync();
        }
        // Bound each insert independently; the first monolithic seed exceeded its 60s
        // call budget on the loaded host. Keep the same dataset and deadline.
        for (var first = 1; first <= 511;)
        {
            var last = Math.Min(first <= 32 ? first + 3 : first + 63, first <= 32 ? 32 : 511);
            await using var seed = new NpgsqlCommand("""
            INSERT INTO "TranscriptEntries" ("Id", "AgentSessionId", "Sequence", "Kind", "Text", "Timestamp", "CreatedAt")
            SELECT (lpad(s::text, 8, '0') || '-0000-0000-0000-' || lpad(n::text, 12, '0'))::uuid, (@ids)[s], n,
                CASE WHEN n % 10 = 0 THEN 'TurnEnd' WHEN n % 3 = 0 THEN 'UserPrompt' ELSE 'AssistantText' END,
                'synthetic', CASE WHEN n % 7 = 0 THEN NULL ELSE timestamptz '2026-01-01' + (n % 5) * interval '1 second' END,
                timestamptz '2026-01-02'
            FROM generate_series(@first, @last) s
            CROSS JOIN LATERAL generate_series(1, CASE WHEN s <= 32 THEN 75000 ELSE 1500 END) n;
            """, connection) { CommandTimeout = 60 };
            seed.Parameters.AddWithValue("ids", ids);
            seed.Parameters.AddWithValue("first", first);
            seed.Parameters.AddWithValue("last", last);
            await seed.ExecuteNonQueryAsync();
            first = last + 1;
        }
        await using (var analyze = new NpgsqlCommand("ANALYZE \"AgentTasks\"; ANALYZE \"AgentSessions\"; ANALYZE \"TranscriptEntries\"; SET jit = off; SET statement_timeout = '60s';", connection) { CommandTimeout = 60 })
            await analyze.ExecuteNonQueryAsync();
        Record("fixture", new { transcriptRows = 3118500, taskRows = 27695, seedMs = timer.Elapsed.TotalMilliseconds,
            load = File.Exists("/proc/loadavg") ? File.ReadAllText("/proc/loadavg") : "unknown",
            inventory = await ScalarAsync(connection, "SELECT jsonb_agg(row_to_json(i))::text FROM (SELECT tablename,indexname,indexdef FROM pg_indexes WHERE schemaname='public' AND tablename IN ('AgentTasks','TranscriptEntries') ORDER BY indexname) i"),
            sizes = await ScalarAsync(connection, "SELECT jsonb_agg(row_to_json(s))::text FROM (SELECT relname,reltuples,pg_relation_size(oid) bytes FROM pg_class WHERE relname IN ('AgentTasks','TranscriptEntries')) s") });
        return (f, ids);
        }
        catch { await f.DisposeAsync(); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        if (_baseline.IsValueCreated) await (await _baseline.Value).World.DisposeAsync();
    }

    private static async Task<string> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 60 };
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static void Record(string name, object value)
    {
        var root = Path.Combine(Environment.GetEnvironmentVariable("ANTIPHON_C1887_EVIDENCE") ?? Path.GetTempPath(), "c1887-pin-plans");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name + ".json"), JsonSerializer.Serialize(value));
    }
}

internal sealed class SessionStateSeedCapture : DbCommandInterceptor
{
    public List<CapturedTranscriptCommand> Commands { get; } = [];
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("session-state.seed") || command.CommandText.Contains("session-state.pins"))
            Commands.Add(new(command.CommandText, command.Parameters.Cast<NpgsqlParameter>().Select(p => p.Clone()).ToArray()));
        return ValueTask.FromResult(result);
    }
}
