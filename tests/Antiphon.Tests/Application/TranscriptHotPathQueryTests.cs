using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[NotInParallel("TranscriptHotPathQueries")]
public class TranscriptHotPathQueryTests
{
    [Test]
    public async Task Uuid_membership_seeks_by_session_and_uuid()
    {
        await using var f = await TranscriptHotPathFixture.CreateAsync(large: true);
        foreach (var count in new[] { 1, 512, 1025 })
        {
            var commands = await f.MembershipAsync(count);
            commands.ShouldNotBeEmpty();
            foreach (var command in commands)
            {
                var plan = await f.ExplainAsync(command);
                f.Record($"uuid-{count}", new { command.Sql, plan });
                TranscriptPlanAssertions.UuidSeek(plan);
            }
            commands.Count.ShouldBe((count + 511) / 512);
            commands.ShouldAllBe(c => c.Parameters.Where(p => p.Value is IEnumerable<string>)
                .All(p => ((IEnumerable<string>)p.Value!).Count() <= 512));
        }
    }

    [Test]
    public async Task Working_batch_uses_one_statement_and_indexed_boundaries()
    {
        await using var f = await TranscriptHotPathFixture.CreateAsync(large: true);
        foreach (var count in new[] { 1, 32 })
        {
            var commands = await f.WorkingAsync(count);
            // Capture baseline plans too, before the intentional two-commands assertion fails.
            var plans = new List<System.Text.Json.JsonElement>();
            foreach (var command in commands) plans.Add(await f.ExplainAsync(command));
            f.Record($"working-{count}", new { commandCount = commands.Count, sql = commands.Select(c => c.Sql), plans });
            commands.Count.ShouldBe(1, "a working batch must read a single statement snapshot");
            TranscriptPlanAssertions.WorkingSeeks(plans.Single());
        }
    }

    [Test]
    public async Task Prepared_plans_keep_the_partial_index_paths()
    {
        await using var f = await TranscriptHotPathFixture.CreateAsync(large: true);
        foreach (var generic in new[] { false, true })
        {
            foreach (var count in new[] { 1, 512, 1025 })
                foreach (var command in await f.MembershipAsync(count))
                {
                    var plan = await f.ExplainAsync(command, generic);
                    f.Record($"prepared-uuid-{count}-{generic}", new { command.Sql, plan });
                    TranscriptPlanAssertions.UuidSeek(plan);
                }
            foreach (var count in new[] { 1, 32 })
            {
                var command = (await f.WorkingAsync(count)).ShouldHaveSingleItem();
                var plan = await f.ExplainAsync(command, generic);
                f.Record($"prepared-working-{count}-{generic}", new { command.Sql, plan });
                TranscriptPlanAssertions.WorkingSeeks(plan);
            }
        }
    }

    [Test]
    public async Task Repeated_hot_reads_do_not_add_transcript_sequential_scans()
    {
        await using var f = await TranscriptHotPathFixture.CreateAsync(large: true);
        // Hold the production connection open so its counters can be flushed before each observation.
        await using var db = f.CreateDb();
        await db.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var uuid = (await f.MembershipAsync(1)).ShouldHaveSingleItem();
        var working = (await f.WorkingAsync(32)).ShouldHaveSingleItem();
        var catchup = await f.MembershipAsync(1025);
        catchup.Count.ShouldBe(3);
        var before = await CountersAsync(f.ConnectionString, connection);
        for (var i = 0; i < 100; i++)
        {
            await ExecuteAsync(connection, uuid);
            await ExecuteAsync(connection, working);
        }
        foreach (var command in catchup) await ExecuteAsync(connection, command);
        var after = await CountersAsync(f.ConnectionString, connection);
        f.Record("scan-counters", new { before, after, delta = after - before, singletonProbes = 100, mixedBatches = 100, catchupKeys = 1025 });
        (after - before).ShouldBe(0);

        // Destructive plan controls are confined to this disposable database.
        await using var drop = new NpgsqlCommand("""
            DROP INDEX "IX_TranscriptEntries_AgentSessionId_Uuid";
            DROP INDEX "IX_TranscriptEntries_End_AgentSessionId_Sequence";
            DROP INDEX "IX_TranscriptEntries_End_AgentSessionId_Timestamp";
            """, connection);
        await drop.ExecuteNonQueryAsync();
        var uuidWithoutIndexes = await f.ExplainAsync(uuid);
        var workingWithoutIndexes = await f.ExplainAsync(working);
        f.Record("dropped-index-controls", new { uuidWithoutIndexes, workingWithoutIndexes });
        Should.Throw<ShouldAssertException>(() => TranscriptPlanAssertions.UuidSeek(uuidWithoutIndexes));
        Should.Throw<ShouldAssertException>(() => TranscriptPlanAssertions.WorkingSeeks(workingWithoutIndexes));
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, CapturedTranscriptCommand captured)
    {
        await using var command = new NpgsqlCommand(captured.Sql, connection);
        command.Parameters.AddRange(captured.Parameters.Select(p => p.Clone()).ToArray());
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) { }
    }

    private static async Task<long> CountersAsync(string connectionString, NpgsqlConnection producer)
    {
        await using (var flush = new NpgsqlCommand("SELECT pg_stat_force_next_flush()", producer))
            await flush.ExecuteNonQueryAsync();
        await using var observer = new NpgsqlConnection(connectionString);
        await observer.OpenAsync();
        await using var read = new NpgsqlCommand("""
            SELECT seq_scan FROM pg_stat_user_tables WHERE relname = 'TranscriptEntries'
            """, observer);
        return (long)(await read.ExecuteScalarAsync())!;
    }
}
