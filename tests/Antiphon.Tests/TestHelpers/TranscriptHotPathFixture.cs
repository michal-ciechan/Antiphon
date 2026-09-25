using System.Data.Common;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

// Each consumer owns a cloned database: ANALYZE, DDL and statistics never touch the shared store.
internal sealed class TranscriptHotPathFixture : IAsyncDisposable
{
    private readonly IsolatedTestSchema _schema;
    private readonly ServiceProvider _provider;
    private readonly string _logPath = Path.Combine(Path.GetTempPath(), $"antiphon-c698-{Guid.NewGuid():N}");
    public TranscriptCommandCapture Capture { get; } = new();
    public Guid[] SessionIds { get; } = Enumerable.Range(0, 130).Select(_ => Guid.NewGuid()).ToArray();
    public string ConnectionString => _schema.ConnectionString;
    public AgentSessionRuntime Runtime { get; }

    private TranscriptHotPathFixture(IsolatedTestSchema schema)
    {
        _schema = schema;
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(b => Configure(b));
        _provider = services.BuildServiceProvider();
        Runtime = new AgentSessionRuntime(new MockEventBus(),
            Options.Create(new AgentSessionSettings { SessionLogPath = _logPath }),
            _provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System,
            NullLogger<AgentSessionRuntime>.Instance);
    }

    public static async Task<TranscriptHotPathFixture> CreateAsync(bool large = false)
    {
        var f = new TranscriptHotPathFixture(await TestDbFixture.CreateIsolatedSchemaAsync());
        try
        {
            await using var db = f.CreateDb();
            db.AgentSessions.AddRange(f.SessionIds.Select(id => new AgentSession
            {
                Id = id, DefinitionName = "c698-synthetic", AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running, Cwd = Path.GetTempPath(), Cols = 120, Rows = 30,
                CreatedAt = DateTime.UtcNow, StartedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow
            }));
            await db.SaveChangesAsync();
            if (large) await f.SeedLargeAsync();
            f.Capture.Clear();
            return f;
        }
        catch
        {
            await f.DisposeAsync();
            throw;
        }
    }

    private void Configure(DbContextOptionsBuilder b) => b.UseNpgsql(ConnectionString,
        o => { o.MigrationsAssembly("Antiphon.Server"); o.SetPostgresVersion(16, 0); })
        .AddInterceptors(Capture);

    public AppDbContext CreateDb()
    {
        var b = new DbContextOptionsBuilder<AppDbContext>();
        Configure(b);
        return new AppDbContext(b.Options);
    }

    public async Task SeedLargeAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO "TranscriptEntries"
                ("Id", "AgentSessionId", "Sequence", "Kind", "Uuid", "Text", "Timestamp", "CreatedAt")
            SELECT gen_random_uuid(), (@ids)[s + 1], n,
                CASE WHEN s = 129 THEN 'QueueEnqueue'
                     WHEN s = 128 THEN 'AssistantText'
                     WHEN s = 1 AND n > 80000 THEN 'AssistantText'
                     WHEN s = 127 AND n > 300 THEN 'AssistantText'
                     WHEN n % 10 = 0 THEN 'TurnEnd'
                     WHEN n % 10 = 1 THEN 'UserPrompt'
                     WHEN n % 10 = 2 THEN 'ToolCall'
                     WHEN n % 10 = 3 THEN 'ToolResult' ELSE 'AssistantText' END,
                CASE WHEN n % 19 = 0 THEN NULL ELSE 'uuid-' || (n / 2)::text END,
                repeat('synthetic transcript ', CASE WHEN n % 7 = 0 THEN 40 ELSE 4 END),
                CASE WHEN (s = 1 AND n > 80000) OR (s = 127 AND n > 300)
                          THEN timestamptz '2026-01-01 00:00:00+00' + interval '1 millisecond'
                     WHEN n % 40 = 0 THEN NULL
                     ELSE timestamptz '2026-01-01 00:00:00+00' +
                        (CASE WHEN (s = 1 AND n > 80000) OR (s = 127 AND n > 300) THEN 1
                              WHEN n % 20 = 0 THEN n - 25 ELSE n END) * interval '1 millisecond' END,
                timestamptz '2026-01-01 00:00:00+00'
            FROM (
                SELECT CASE WHEN g <= 200000 THEN 0 WHEN g <= 300000 THEN 1
                            ELSE 2 + (g - 300001) % 128 END AS s,
                       CASE WHEN g <= 200000 THEN g WHEN g <= 300000 THEN g - 200000
                            ELSE 1 + (g - 300001) / 128 END AS n
                FROM generate_series(1, 377000) AS g
            ) AS source;
            ANALYZE "TranscriptEntries";
            """, connection) { CommandTimeout = 120 };
        command.Parameters.AddWithValue("ids", SessionIds);
        var clock = Stopwatch.StartNew();
        await command.ExecuteNonQueryAsync();
        Record("seed", new { rows = 377000, elapsedMs = clock.ElapsedMilliseconds });
    }

    public async Task<IReadOnlyList<CapturedTranscriptCommand>> MembershipAsync(int keys)
    {
        Capture.Clear();
        var entries = Enumerable.Range(0, keys).Select(n => new SessionRunnerTranscriptEvent(
            SessionIds[0], n + 1, TranscriptKinds.AssistantText, n % 97 == 0 ? $"missing-{n}" : $"uuid-{n}", null,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), "assistant", "synthetic", null, null,
            null, null, null)).ToArray();
        await Runtime.PersistTranscriptAsync(SessionIds[0], entries);
        Runtime.TryGetTranscriptPersistFailure(SessionIds[0], out _).ShouldBeFalse();
        return Capture.Membership.ToArray();
    }

    public async Task<IReadOnlyList<CapturedTranscriptCommand>> WorkingAsync(int count)
    {
        Capture.Clear();
        await using var db = CreateDb();
        // Include the large sessions, stale tail, housekeeping-only and no-end cases.
        var ids = count == 1 ? [SessionIds[0]] : SessionIds.Take(count - 3).Concat(SessionIds.TakeLast(3)).ToArray();
        var result = await SessionMessageQueueService.IsWorkingBatchAsync(db, ids, default);
        result.Count.ShouldBe(count);
        result[SessionIds[0]].ShouldBeFalse();
        if (count > 1)
        {
            result[SessionIds[1]].ShouldBeFalse("the 20k post-end tail is entirely timestamp-proven stale");
            result[SessionIds[^3]].ShouldBeFalse();
            result[SessionIds[^2]].ShouldBeTrue("no-end activity is still working");
            result[SessionIds[^1]].ShouldBeFalse("no-end housekeeping stays idle");
        }
        return Capture.Reads.ToArray();
    }

    public async Task<JsonElement> ExplainAsync(CapturedTranscriptCommand captured, bool generic = false)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand { Connection = connection, CommandTimeout = 120 };
        if (generic)
        {
            // PREPARE the actual SELECT, not EXPLAIN itself. Keep typed parameters in its plan.
            var sql = captured.Sql;
            for (var i = 0; i < captured.Parameters.Length; i++)
                sql = Regex.Replace(sql, "@" + Regex.Escape(captured.Parameters[i].ParameterName.TrimStart('@', ':')) + @"\b",
                    _ => "$" + (i + 1));
            var types = captured.Parameters.Select(p => p.Value switch
            {
                Guid => "uuid", Guid[] => "uuid[]", string => "text",
                IEnumerable<string> => "text[]", _ => throw new NotSupportedException(p.NpgsqlDbType.ToString())
            });
            command.CommandText = $"DEALLOCATE ALL; SET plan_cache_mode = force_generic_plan; PREPARE c698 ({string.Join(",", types)}) AS {sql}";
            await command.ExecuteNonQueryAsync();
            command.CommandText = "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) EXECUTE c698 (" +
                string.Join(",", captured.Parameters.Select(p => Literal(p.Value))) + ")";
        }
        else
        {
            command.CommandText = "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + captured.Sql;
            command.Parameters.AddRange(captured.Parameters.Select(p => p.Clone()).ToArray());
        }
        using var json = JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
        if (generic)
        {
            command.CommandText = "SELECT generic_plans FROM pg_prepared_statements WHERE name = 'c698'";
            ((long)(await command.ExecuteScalarAsync())!).ShouldBeGreaterThan(0,
                "EXPLAIN must execute the separately prepared generic SELECT");
        }
        return json.RootElement.Clone();
    }

    private static string Literal(object? value) => value switch
    {
        Guid g => $"'{g}'::uuid",
        string s => "'" + s.Replace("'", "''") + "'::text",
        Guid[] ids => "ARRAY[" + string.Join(",", ids.Select(g => Literal(g))) + "]::uuid[]",
        IEnumerable<string> keys => "ARRAY[" + string.Join(",", keys.Select(s => Literal(s))) + "]::text[]",
        _ => throw new NotSupportedException("Unexpected synthetic parameter type")
    };

    public void Record(string name, object value)
    {
        var root = Environment.GetEnvironmentVariable("C698_EVIDENCE_ROOT") ??
            Path.Combine(AppContext.BaseDirectory, "TestResults", "card0698");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, $"{name}-{SessionIds[0]:N}.json"),
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var id in SessionIds) await Runtime.DisposeSessionAsync(id);
        await _provider.DisposeAsync();
        await _schema.DisposeAsync();
        if (Directory.Exists(_logPath)) Directory.Delete(_logPath, true);
    }
}

internal sealed record CapturedTranscriptCommand(string Sql, NpgsqlParameter[] Parameters)
{
    public object Evidence() => new { Sql, parameters = Parameters.Select(p =>
        new { p.ParameterName, type = p.NpgsqlDbType.ToString(), clrType = p.Value?.GetType().FullName, p.Value }) };
}

internal sealed class TranscriptCommandCapture : DbCommandInterceptor
{
    private readonly ConcurrentQueue<CapturedTranscriptCommand> _commands = new();
    public IEnumerable<CapturedTranscriptCommand> Reads => _commands.Where(c =>
        c.Sql.Contains("\"TranscriptEntries\"", StringComparison.Ordinal) &&
        Regex.IsMatch(c.Sql, @"(?m)^\s*SELECT\b"));
    public IEnumerable<CapturedTranscriptCommand> Membership => Reads.Where(c =>
        c.Sql.Contains("t.\"Uuid\", t.\"Kind\"", StringComparison.Ordinal));
    public void Clear() => _commands.Clear();

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        _commands.Enqueue(new CapturedTranscriptCommand(command.CommandText,
            command.Parameters.Cast<NpgsqlParameter>().Select(p => p.Clone()).ToArray()));
        return ValueTask.FromResult(result);
    }
}

internal static class TranscriptPlanAssertions
{
    public static IEnumerable<JsonElement> Nodes(JsonElement plan)
    {
        if (plan.ValueKind == JsonValueKind.Array)
            return plan.EnumerateArray().SelectMany(Nodes);
        if (plan.ValueKind != JsonValueKind.Object) return [];
        return (plan.TryGetProperty("Node Type", out _) ? new[] { plan } : [])
            .Concat(plan.EnumerateObject().Where(p => p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                .SelectMany(p => Nodes(p.Value)));
    }

    public static string Property(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) ? value.ToString() : "";

    public static void NoTranscriptSequentialScan(JsonElement plan) => Nodes(plan).ShouldNotContain(n =>
        Property(n, "Relation Name") == "TranscriptEntries" && Property(n, "Node Type").Contains("Seq Scan"));

    public static void UuidSeek(JsonElement plan)
    {
        Nodes(plan).ShouldContain(n =>
            Property(n, "Index Name") == "IX_TranscriptEntries_AgentSessionId_Uuid" &&
            Property(n, "Index Cond").Contains("AgentSessionId") && Property(n, "Index Cond").Contains("Uuid"),
            "UUID membership must seek by BOTH session and UUID, not scan a session's history");
        NoTranscriptSequentialScan(plan);
    }

    public static void WorkingSeeks(JsonElement plan)
    {
        NoTranscriptSequentialScan(plan);
        var nodes = Nodes(plan).ToArray();
        nodes.ShouldNotContain(n => Property(n, "Node Type").Contains("Aggregate"));
        foreach (var suffix in new[] { "Sequence", "Timestamp" })
            nodes.ShouldContain(n => Property(n, "Node Type") == "Limit" && Nodes(n).Any(c =>
                Property(c, "Index Name") == "IX_TranscriptEntries_End_AgentSessionId_" + suffix &&
                Property(c, "Index Cond").Contains("AgentSessionId")), "boundary must use a top-one partial-index probe");
        nodes.ShouldContain(n => Property(n, "Index Name") == "IX_TranscriptEntries_AgentSessionId_Sequence" &&
            Regex.IsMatch(Property(n, "Index Cond"), "\"Sequence\"\\s*>"), "post-end activity must seek the sequence range");
    }
}
