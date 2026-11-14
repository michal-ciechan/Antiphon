using System.Diagnostics;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[NotInParallel]
[ClassDataSource<SessionStateScaleFixture>(Shared = SharedType.PerTestSession)]
public sealed class SessionTranscriptSummaryAppendCostTests(SessionStateScaleFixture scale)
{
    [Test]
    public async Task Feature_off_append_options_record_scale_costs()
    {
        var (world, ids) = await scale.GetAsync();
        await using var db = world.Db();
        await db.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var installed = (await db.Database.SqlQueryRaw<string>(
            "SELECT pg_get_functiondef('c1915_insert()'::regprocedure) AS \"Value\"").SingleAsync())
            .Replace("CREATE FUNCTION", "CREATE OR REPLACE FUNCTION", StringComparison.Ordinal);
        // Candidate-only DDL stays inside this fixture's owned clone and rolls back.
        // The gate candidate measures a one-row database switch, not a safe activation API.
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE \"C1915GateCandidate\" (\"Enabled\" boolean NOT NULL); INSERT INTO \"C1915GateCandidate\" VALUES (false)");
        var probe = ProbeCandidate(installed);
        var gated = installed.Replace("BEGIN\n", "BEGIN\n IF NOT (SELECT \"Enabled\" FROM \"C1915GateCandidate\") THEN RETURN NULL; END IF;\n", StringComparison.Ordinal);
        var results = new List<object>();
        var variants = installed == probe
            ? new[] { "dropped", "index-probe", "database-gate" }
            : new[] { "dropped", "index-probe", "database-gate", "installed" };
        for (var round = 0; round < 3; round++)
        foreach (var state in new[] { "absent", "invalid" })
        foreach (var variant in variants.Skip(round).Concat(variants.Take(round)))
        {
            await using var tx = await connection.BeginTransactionAsync();
            if (variant == "dropped")
                await ExecuteAsync("DROP TRIGGER c1915_insert ON \"TranscriptEntries\"; DROP TRIGGER c1915_update ON \"TranscriptEntries\"; DROP TRIGGER c1915_delete ON \"TranscriptEntries\"; DROP TRIGGER c1915_truncate ON \"TranscriptEntries\"");
            else
                await ExecuteAsync(variant == "index-probe" ? probe : variant == "database-gate" ? gated : installed);
            var id = ids[^1]; // The fixture's empty session; the other 511 retain 3,118,500 rows.
            if (state == "invalid")
                await ExecuteAsync("INSERT INTO \"SessionTranscriptSummaries\" (\"AgentSessionId\",\"SchemaVersion\",\"Valid\",\"Count\",\"LastSequence\",\"HasActivity\",\"HasNullTimestampActivity\") VALUES (@id,1,false,0,0,false,false)", id);
            await using var singleton = new NpgsqlCommand("INSERT INTO \"TranscriptEntries\" (\"Id\",\"AgentSessionId\",\"Sequence\",\"Kind\",\"CreatedAt\") VALUES (gen_random_uuid(),@id,@seq,'AssistantText',timestamptz '2026-01-02')", connection, tx) { CommandTimeout = 60 };
            singleton.Parameters.AddWithValue("id", id);
            singleton.Parameters.AddWithValue("seq", 0L);
            await singleton.PrepareAsync();
            // Warm the function/query plans, without turning the absent case into invalid.
            for (long n = 1; n <= 10; n++) { singleton.Parameters["seq"].Value = n; await singleton.ExecuteNonQueryAsync(); }
            if (state == "absent") await ExecuteAsync("DELETE FROM \"SessionTranscriptSummaries\" WHERE \"AgentSessionId\"=@id", id);
            var load = File.Exists("/proc/loadavg") ? await File.ReadAllTextAsync("/proc/loadavg") : "unknown";
            var timer = Stopwatch.StartNew();
            for (long n = 11; n <= 110; n++) { singleton.Parameters["seq"].Value = n; await singleton.ExecuteNonQueryAsync(); }
            var singletonMs = timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            await ExecuteAsync("INSERT INTO \"TranscriptEntries\" (\"Id\",\"AgentSessionId\",\"Sequence\",\"Kind\",\"CreatedAt\") SELECT gen_random_uuid(),@id,n,'AssistantText',timestamptz '2026-01-02' FROM generate_series(111,1110) n", id);
            var multirowMs = timer.Elapsed.TotalMilliseconds;
            (await CountAsync()).ShouldBe(1110, "the measurement must execute all 100 singleton statements and the 1000-row statement");
            var result = new { round, state, variant, singletonStatements = 100, multirowStatements = 1, multirowRows = 1000, singletonMs, multirowMs, load = load.Trim() };
            results.Add(result);
            Console.WriteLine("C1915 COST " + JsonSerializer.Serialize(result));
            await tx.RollbackAsync();

            async Task ExecuteAsync(string sql, Guid? session = null)
            {
                await using var command = new NpgsqlCommand(sql, connection, tx) { CommandTimeout = 60 };
                if (session is { } value) command.Parameters.AddWithValue("id", value);
                await command.ExecuteNonQueryAsync();
            }
            async Task<long> CountAsync()
            {
                await using var command = new NpgsqlCommand("SELECT count(*) FROM \"TranscriptEntries\" WHERE \"AgentSessionId\"=@id", connection, tx) { CommandTimeout = 60 };
                command.Parameters.AddWithValue("id", id);
                return (long)(await command.ExecuteScalarAsync())!;
            }
        }
        var root = Path.Combine(Environment.GetEnvironmentVariable("ANTIPHON_C1887_EVIDENCE") ?? Path.GetTempPath(), "c1915-append-cost");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "options.json"), JsonSerializer.Serialize(results));
    }

    private static string ProbeCandidate(string definition)
    {
        var start = definition.IndexOf("-- Statement order", StringComparison.Ordinal);
        if (start < 0) return definition; // The installed production version already is the probe candidate.
        var end = definition.IndexOf("IF NOT previous_row.\"Valid\"", start, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start);
        return definition[..start] + """
            -- Probe candidates without writing or locking absent/invalid rows.
            FOR session_id IN
                SELECT DISTINCT n."AgentSessionId" FROM new_entries n
                JOIN "SessionTranscriptSummaries" s ON s."AgentSessionId" = n."AgentSessionId"
                WHERE s."Valid" ORDER BY 1
            LOOP
                SELECT * INTO previous_row FROM "SessionTranscriptSummaries"
                WHERE "AgentSessionId" = session_id FOR UPDATE;
                IF NOT FOUND THEN CONTINUE; END IF;
            """ + "\n" + definition[end..];
    }
}
