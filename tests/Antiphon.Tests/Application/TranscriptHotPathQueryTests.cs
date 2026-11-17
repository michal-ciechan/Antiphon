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
                var index = Array.IndexOf(commands.ToArray(), command);
                f.Record($"uuid-{count}-{index}", new { command = command.Evidence(), plan });
                TranscriptPlanAssertions.UuidSeek(plan);
                TranscriptPlanAssertions.PrefixCountReadsOnlySession(plan, f.SessionIds[0]);
                // CARD-1888: a batch of 64 or more is a memo record candidate, so its prelude carries
                // the prefix count; the scoped guard above must have something to scope.
                TranscriptPlanAssertions.PrefixCountScans(plan).Any().ShouldBe(index == 0 && count >= 64);
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
            f.Record($"working-{count}", new { commandCount = commands.Count, commands = commands.Select(c => c.Evidence()), plans });
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
                foreach (var (command, index) in (await f.MembershipAsync(count)).Select((c, i) => (c, i)))
                {
                    var plan = await f.ExplainAsync(command, generic);
                    f.Record($"prepared-uuid-{count}-{generic}-{index}", new { command = command.Evidence(), plan });
                    TranscriptPlanAssertions.UuidSeek(plan);
                    TranscriptPlanAssertions.PrefixCountReadsOnlySession(plan, f.SessionIds[0]);
                    TranscriptPlanAssertions.PrefixCountScans(plan).Any().ShouldBe(index == 0 && count >= 64);
                }
            foreach (var count in new[] { 1, 32 })
            {
                var command = (await f.WorkingAsync(count)).ShouldHaveSingleItem();
                var plan = await f.ExplainAsync(command, generic);
                f.Record($"prepared-working-{count}-{generic}", new { command = command.Evidence(), plan });
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
        // CARD-1888: the catch-up prelude (catchup[0]) also carries the plan-approved part-0 prefix
        // count, which reads this whole session (a parallel Seq Scan on the fixture's 53%-of-table
        // session). The zero-delta window covers the UUID probe branches only: the singleton probe,
        // the working batch and the two pure membership chunks. The prelude's own probe branch keeps
        // the scoped plan guard below, and its count must read only this session. Scoping approved
        // by the orchestrator; see TranscriptPlanAssertions.PrefixCountScans. The prelude is explained
        // only after the window closes, so its count never runs inside it.
        // CARD-1888 round 2: the setup above ran that count, so its scans must be published before
        // the window opens, or they land inside it (review d1a53b65: Sequential 2 -> 4).
        await FlushIdleBackendsAsync(f.ConnectionString, connection);
        var before = await CountersAsync(f.ConnectionString, connection);
        for (var i = 0; i < 100; i++)
        {
            await ExecuteAsync(connection, uuid);
            await ExecuteAsync(connection, working);
        }
        foreach (var command in catchup.Skip(1)) await ExecuteAsync(connection, command);
        var after = await CountersAsync(f.ConnectionString, connection, before.Indexed + 202);
        f.Record("scan-counters", new { before, after, delta = after.Sequential - before.Sequential, singletonProbes = 100, mixedBatches = 100, catchupChunks = catchup.Count - 1, catchupKeys = 1025 });
        (after.Sequential - before.Sequential).ShouldBe(0);
        var prelude = await f.ExplainAsync(catchup[0]);
        f.Record("catchup-prelude", new { command = catchup[0].Evidence(), prelude });
        TranscriptPlanAssertions.UuidSeek(prelude);
        TranscriptPlanAssertions.PrefixCountScans(prelude).ShouldNotBeEmpty();
        TranscriptPlanAssertions.PrefixCountReadsOnlySession(prelude, f.SessionIds[0]);

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

    // The setup statements ran on backends now idle in Npgsql's pool. PostgreSQL 16 publishes a
    // backend's table counters when it goes idle, but only if it has not flushed within the last
    // second (PGSTAT_MIN_INTERVAL); otherwise it defers them to a 10 s idle timer, which can fire inside
    // the window. Polling until the counters look stable cannot tell a deferred flush from none.
    // pg_stat_force_next_flush() makes its own backend flush before it reports ReadyForQuery, and the
    // pool hands out idle connectors before it opens a new one, so every idle backend of this
    // database is checked out and flushed. Parallel workers need nothing: each flushes on exit, and
    // the leader's Gather waits for that exit before its statement completes.
    private static async Task FlushIdleBackendsAsync(string connectionString, NpgsqlConnection producer)
    {
        var held = new List<NpgsqlConnection>();
        try
        {
            var flushed = new HashSet<int> { producer.ProcessID };
            HashSet<int>? census = null;
            while (true)
            {
                var connection = new NpgsqlConnection(connectionString);
                held.Add(connection);
                await connection.OpenAsync();
                if (census is null)
                {
                    census = [];
                    await using var list = new NpgsqlCommand("""
                        SELECT pid FROM pg_stat_activity
                        WHERE datname = current_database() AND backend_type = 'client backend'
                        """, connection);
                    await using var reader = await list.ExecuteReaderAsync();
                    while (await reader.ReadAsync()) census.Add(reader.GetInt32(0));
                }
                // A backend outside the census is a new connector: the pool has no idle one left.
                if (!census.Contains(connection.ProcessID)) break;
                await using (var flush = new NpgsqlCommand("SELECT pg_stat_force_next_flush()", connection))
                    await flush.ExecuteNonQueryAsync();
                flushed.Add(connection.ProcessID);
                if (census.IsSubsetOf(flushed)) break;
            }
            census!.Except(flushed).ShouldBeEmpty(
                "every backend that ran a setup statement must flush its scan counters before the window opens");
        }
        finally
        {
            foreach (var connection in held) await connection.DisposeAsync();
        }
    }

    private sealed record Counters(long Sequential, long Indexed);

    private static async Task<Counters> CountersAsync(string connectionString, NpgsqlConnection producer, long minimumIndexed = 0)
    {
        await using (var flush = new NpgsqlCommand("SELECT pg_stat_force_next_flush()", producer))
            await flush.ExecuteNonQueryAsync();
        await using var observer = new NpgsqlConnection(connectionString);
        await observer.OpenAsync();
        // A positive index-counter delta proves the producer's statistics reached this fresh
        // observer; zero sequential scans alone could otherwise be an unpublished snapshot.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            await using var read = new NpgsqlCommand("""
                SELECT seq_scan, COALESCE(idx_scan, 0) FROM pg_stat_user_tables WHERE relname = 'TranscriptEntries'
                """, observer);
            Counters counters;
            await using (var reader = await read.ExecuteReaderAsync())
            {
                (await reader.ReadAsync()).ShouldBeTrue();
                counters = new(reader.GetInt64(0), reader.GetInt64(1));
            }
            if (counters.Indexed >= minimumIndexed) return counters;
            clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10), "the producer's scan counters must be published");
            await Task.Delay(50);
        }
    }
}
