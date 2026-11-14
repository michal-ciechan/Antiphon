using System.Data.Common;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
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
public sealed class SessionTranscriptSummaryAppendTests
{
    [Test]
    public async Task Absent_and_invalid_summaries_receive_no_write_statements()
    {
        await using var f = await SessionStateTestFixture.CreateAsync();
        await using var db = f.Db();
        (await db.Database.SqlQueryRaw<string>("SELECT tgenabled::text AS \"Value\" FROM pg_trigger WHERE tgrelid='\"TranscriptEntries\"'::regclass AND tgname='c1915_insert'").SingleAsync()).ShouldBe("D");
        var retryAt = Epoch.AddYears(10);
        db.SessionTranscriptSummaries.Add(new SessionTranscriptSummary {
            AgentSessionId = f.OtherId, Valid = false, NextBackfillAttemptAt = retryAt });
        await db.SaveChangesAsync();
        var version = await db.Database.SqlQueryRaw<string>(
            "SELECT xmin::text AS \"Value\" FROM \"SessionTranscriptSummaries\" WHERE \"AgentSessionId\"=@id",
            new NpgsqlParameter("id", f.OtherId)).SingleAsync();
        // Statement triggers see attempted writes too, including INSERT ... ON CONFLICT
        // DO NOTHING and UPDATEs matching zero rows. xmin alone would miss those.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE "C1915SummaryWrites" ("Operation" text NOT NULL);
            CREATE FUNCTION c1915_summary_write_audit() RETURNS trigger LANGUAGE plpgsql AS $audit$
            BEGIN INSERT INTO "C1915SummaryWrites" VALUES (TG_OP); RETURN NULL; END; $audit$;
            CREATE TRIGGER c1915_summary_write_audit AFTER INSERT OR UPDATE OR DELETE
                ON "SessionTranscriptSummaries" FOR EACH STATEMENT EXECUTE FUNCTION c1915_summary_write_audit();
            """);
        foreach (var id in new[] { f.SessionId, f.OtherId })
        for (var n = 1; n <= 100; n++)
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"TranscriptEntries\" (\"Id\",\"AgentSessionId\",\"Sequence\",\"Kind\",\"CreatedAt\") VALUES ({Guid.NewGuid()},{id},{n},'AssistantText',{Epoch})");
        (await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM \"C1915SummaryWrites\"").SingleAsync())
            .ShouldBe(0, "100 absent and 100 invalid appends must issue zero summary write statements");
        (await db.SessionTranscriptSummaries.AnyAsync(s => s.AgentSessionId == f.SessionId)).ShouldBeFalse();
        var invalid = await SummaryAsync(db, f.OtherId);
        invalid.Valid.ShouldBeFalse();
        invalid.NextBackfillAttemptAt.ShouldBe(retryAt);
        (await db.Database.SqlQueryRaw<string>(
            "SELECT xmin::text AS \"Value\" FROM \"SessionTranscriptSummaries\" WHERE \"AgentSessionId\"=@id",
            new NpgsqlParameter("id", f.OtherId)).SingleAsync()).ShouldBe(version);
        var cold = await f.Services.GetRequiredService<ISessionStateLoader>().LoadAsync([f.SessionId, f.OtherId], default);
        cold.Values.ShouldAllBe(s => s.Count == 100);
    }

    [Test]
    public async Task Invalid_summary_row_locks_do_not_block_appends()
    {
        await using var f = await SessionStateTestFixture.CreateAsync();
        await using var db = f.Db();
        db.SessionTranscriptSummaries.Add(new SessionTranscriptSummary { AgentSessionId = f.SessionId });
        await db.SaveChangesAsync();
        await using var blocker = new NpgsqlConnection(db.Database.GetConnectionString());
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using var hold = new NpgsqlCommand("SELECT 1 FROM \"SessionTranscriptSummaries\" WHERE \"AgentSessionId\"=@id FOR UPDATE", blocker, transaction) { CommandTimeout = 10 };
        hold.Parameters.AddWithValue("id", f.SessionId);
        await hold.ExecuteScalarAsync();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET lock_timeout='1s'");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"TranscriptEntries\" (\"Id\",\"AgentSessionId\",\"Sequence\",\"Kind\",\"CreatedAt\") VALUES ({Guid.NewGuid()},{f.SessionId},1,'AssistantText',{Epoch})");
        (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == f.SessionId)).ShouldBe(1);
        (await SummaryAsync(db, f.SessionId)).Valid.ShouldBeFalse();
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task Existing_valid_summaries_stay_current_without_an_enabled_reader()
    {
        // S1-S3 has no durable-summary reader or activation flag. D-7 requires
        // maintenance to be independent of the reader, including its disabled state.
        await using var f = await SessionStateTestFixture.CreateAsync();
        await using var db = f.Db();
        await SessionTranscriptSummaryMigrationFixture.SetMaintenanceAsync(db, true);
        db.SessionTranscriptSummaries.Add(new SessionTranscriptSummary { AgentSessionId = f.SessionId, Valid = true });
        await db.SaveChangesAsync();
        for (var n = 1; n <= 2; n++)
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"TranscriptEntries\" (\"Id\",\"AgentSessionId\",\"Sequence\",\"Kind\",\"CreatedAt\") VALUES ({Guid.NewGuid()},{f.SessionId},{n},'AssistantText',{Epoch})");
        await EqualAsync(db, [f.SessionId]);
        (await SummaryAsync(db, f.SessionId)).Count.ShouldBe(2);
        (await f.Services.GetRequiredService<ISessionStateLoader>().LoadAsync([f.SessionId], default))[f.SessionId].Count.ShouldBe(2);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"TranscriptEntries\" (\"Id\",\"AgentSessionId\",\"Sequence\",\"Kind\",\"CreatedAt\") VALUES ({Guid.NewGuid()},{f.SessionId},0,'TurnEnd',{Epoch})");
        (await SummaryAsync(db, f.SessionId)).Valid.ShouldBeFalse("out-of-order appends still invalidate while the reader is off");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Maintenance_disable_never_leaves_stale_valid_rows(bool rollback)
    {
        await using var f = await SessionStateTestFixture.CreateAsync();
        await using var db = f.Db();
        await SessionTranscriptSummaryMigrationFixture.SetMaintenanceAsync(db, true);
        db.SessionTranscriptSummaries.Add(new SessionTranscriptSummary { AgentSessionId = f.SessionId, Valid = true });
        await db.SaveChangesAsync();
        await AppendRowsAsync(db, new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = f.SessionId, Sequence = 1, Kind = "AssistantText", CreatedAt = Epoch });
        await EqualAsync(db, [f.SessionId]);
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await SessionTranscriptSummaryMigrationFixture.SetMaintenanceAsync(db, false);
            (await SummaryAsync(db, f.SessionId)).Valid.ShouldBeFalse("gate-off invalidation belongs to the same transaction as trigger disablement");
            if (rollback) await transaction.RollbackAsync();
            else await transaction.CommitAsync();
        }
        var version = await db.Database.SqlQueryRaw<string>("SELECT xmin::text AS \"Value\" FROM \"SessionTranscriptSummaries\" WHERE \"AgentSessionId\"=@id", new NpgsqlParameter("id", f.SessionId)).SingleAsync();
        await AppendRowsAsync(db, new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = f.SessionId, Sequence = 2, Kind = "TurnEnd", CreatedAt = Epoch });
        if (rollback)
        {
            (await db.Database.SqlQueryRaw<bool>("SELECT \"Enabled\" AS \"Value\" FROM \"SessionTranscriptSummaryMaintenance\" WHERE \"Id\"").SingleAsync()).ShouldBeTrue();
            await EqualAsync(db, [f.SessionId]);
        }
        else
        {
            (await SummaryAsync(db, f.SessionId)).Valid.ShouldBeFalse();
            (await db.Database.SqlQueryRaw<string>("SELECT xmin::text AS \"Value\" FROM \"SessionTranscriptSummaries\" WHERE \"AgentSessionId\"=@id", new NpgsqlParameter("id", f.SessionId)).SingleAsync()).ShouldBe(version);
            await SessionTranscriptSummaryMigrationFixture.SetMaintenanceAsync(db, true);
            (await SummaryAsync(db, f.SessionId)).Valid.ShouldBeFalse("re-enable cannot invent a backfill or certify stale fields");
        }
        (await f.Services.GetRequiredService<ISessionStateLoader>().LoadAsync([f.SessionId], default))[f.SessionId].Count.ShouldBe(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Maintenance_transitions_serialize_with_appends(bool transitionFirst)
    {
        await using var f = await SessionStateTestFixture.CreateAsync();
        await using var db = f.Db();
        await SessionTranscriptSummaryMigrationFixture.SetMaintenanceAsync(db, true);
        db.SessionTranscriptSummaries.Add(new SessionTranscriptSummary { AgentSessionId = f.SessionId, Valid = true });
        await db.SaveChangesAsync();
        await using var writer = new NpgsqlConnection(db.Database.GetConnectionString());
        await using var gate = new NpgsqlConnection(db.Database.GetConnectionString());
        await using var observer = new NpgsqlConnection(db.Database.GetConnectionString());
        await Task.WhenAll(writer.OpenAsync(), gate.OpenAsync(), observer.OpenAsync());
        await using var writeTx = await writer.BeginTransactionAsync();
        await using var gateTx = await gate.BeginTransactionAsync();
        await using var insert = new NpgsqlCommand("INSERT INTO \"TranscriptEntries\" (\"Id\",\"AgentSessionId\",\"Sequence\",\"Kind\",\"CreatedAt\") VALUES (gen_random_uuid(),@id,1,'AssistantText',timestamptz '2026-01-01')", writer, writeTx) { CommandTimeout = 10 };
        insert.Parameters.AddWithValue("id", f.SessionId);
        await using var disable = new NpgsqlCommand("UPDATE \"SessionTranscriptSummaryMaintenance\" SET \"Enabled\"=false WHERE \"Id\"", gate, gateTx) { CommandTimeout = 10 };
        Task? pending = null;
        var writeDone = false;
        var gateDone = false;
        try
        {
            if (transitionFirst)
            {
                await disable.ExecuteNonQueryAsync();
                pending = insert.ExecuteNonQueryAsync();
                await BlockedAsync(observer, writer.ProcessID, gate.ProcessID);
                await gateTx.CommitAsync(); gateDone = true;
                await pending;
                await writeTx.CommitAsync(); writeDone = true;
            }
            else
            {
                await insert.ExecuteNonQueryAsync();
                pending = disable.ExecuteNonQueryAsync();
                await BlockedAsync(observer, gate.ProcessID, writer.ProcessID);
                await writeTx.CommitAsync(); writeDone = true;
                await pending;
                await gateTx.CommitAsync(); gateDone = true;
            }
            (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == f.SessionId)).ShouldBe(1);
            (await SummaryAsync(db, f.SessionId)).Valid.ShouldBeFalse();
            (await SummaryAsync(db, f.SessionId)).Count.ShouldBe(transitionFirst ? 0 : 1);
            await AppendRowsAsync(db, new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = f.SessionId, Sequence = 2, Kind = "AssistantText", CreatedAt = Epoch });
            (await SummaryAsync(db, f.SessionId)).Valid.ShouldBeFalse();
            (await f.Services.GetRequiredService<ISessionStateLoader>().LoadAsync([f.SessionId], default))[f.SessionId].Count.ShouldBe(2);
        }
        finally
        {
            // Release the blocking transaction before joining the command on the other
            // connection. No pending command or transaction may outlive this test.
            if (transitionFirst) { if (!gateDone) await gateTx.RollbackAsync(); }
            else { if (!writeDone) await writeTx.RollbackAsync(); }
            if (pending is not null) { try { await pending; } catch { /* preserve the original assertion/command failure */ } }
            if (transitionFirst) { if (!writeDone) await writeTx.RollbackAsync(); }
            else { if (!gateDone) await gateTx.RollbackAsync(); }
        }
    }

    [Test]
    public async Task Maintenance_admission_preserves_a_shorter_lock_deadline()
    {
        await using var f = await SessionStateTestFixture.CreateAsync();
        await using var db = f.Db();
        await SessionTranscriptSummaryMigrationFixture.SetMaintenanceAsync(db, true);
        await db.Database.OpenConnectionAsync();
        foreach (var (configured, expected) in new[] { ("0", "5s"), ("1s", "1s"), ("10s", "5s") })
        {
            await using var probe = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('lock_timeout',{configured},true)");
            await SessionTranscriptSummaryMigrationFixture.SetMaintenanceAsync(db, false);
            (await db.Database.SqlQueryRaw<string>("SELECT current_setting('lock_timeout') AS \"Value\"").SingleAsync()).ShouldBe(expected);
            await probe.RollbackAsync();
        }
        await using var blocker = new NpgsqlConnection(db.Database.GetConnectionString());
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using var hold = new NpgsqlCommand("LOCK TABLE \"TranscriptEntries\" IN ROW EXCLUSIVE MODE", blocker, transaction) { CommandTimeout = 10 };
        await hold.ExecuteNonQueryAsync();
        await db.Database.ExecuteSqlRawAsync("SET lock_timeout='1s'");
        var error = await Should.ThrowAsync<PostgresException>(() => SessionTranscriptSummaryMigrationFixture.SetMaintenanceAsync(db, false));
        error.SqlState.ShouldBe(PostgresErrorCodes.LockNotAvailable);
        (await db.Database.SqlQueryRaw<bool>("SELECT \"Enabled\" AS \"Value\" FROM \"SessionTranscriptSummaryMaintenance\" WHERE \"Id\"").SingleAsync()).ShouldBeTrue("refused admission must roll back the whole gate change");
        (await db.Database.SqlQueryRaw<string>("SELECT tgenabled::text AS \"Value\" FROM pg_trigger WHERE tgrelid='\"TranscriptEntries\"'::regclass AND tgname='c1915_insert'").SingleAsync()).ShouldBe("O");
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("DELETE FROM \"SessionTranscriptSummaryMaintenance\"")]
    [Arguments("TRUNCATE \"SessionTranscriptSummaryMaintenance\"")]
    [Arguments("INSERT INTO \"SessionTranscriptSummaryMaintenance\" DEFAULT VALUES")]
    public async Task Maintenance_singleton_cannot_be_removed_or_replaced(string sql)
    {
        await using var f = await SessionStateTestFixture.CreateAsync();
        await using var db = f.Db();
        var error = await Should.ThrowAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
        error.SqlState.ShouldBe(PostgresErrorCodes.ObjectNotInPrerequisiteState);
        (await db.Database.SqlQueryRaw<bool>("SELECT \"Enabled\" AS \"Value\" FROM \"SessionTranscriptSummaryMaintenance\" WHERE \"Id\"").SingleAsync()).ShouldBeFalse();
    }

    private static async Task BlockedAsync(NpgsqlConnection observer, int waiter, int owner)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var command = new NpgsqlCommand("SELECT @owner = ANY(pg_blocking_pids(@waiter))", observer) { CommandTimeout = 10 };
        command.Parameters.AddWithValue("owner", owner); command.Parameters.AddWithValue("waiter", waiter);
        while (!(bool)(await command.ExecuteScalarAsync(budget.Token))!) await Task.Yield();
    }

    [Test]
    [Arguments("normal")]
    [Arguments("ef-single")]
    [Arguments("rollback")]
    [Arguments("ambiguous")]
    [Arguments("partial")]
    [Arguments("stub")]
    public async Task Monotonic_append_folds_only_committed_rows(string scenario)
    {
        var fault = new PersistenceFault(scenario);
        await using var f = await SessionStateTestFixture.CreateAsync(fault);
        await using var db = f.Db();
        await SessionTranscriptSummaryMigrationFixture.SetMaintenanceAsync(db, true);
        db.SessionTranscriptSummaries.Add(new SessionTranscriptSummary { AgentSessionId = f.SessionId, Valid = true });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"TranscriptEntries\" (\"Id\",\"AgentSessionId\",\"Sequence\",\"Kind\",\"CreatedAt\") VALUES ({Guid.NewGuid()},{f.SessionId},1,'AssistantText',now())");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"TranscriptEntries\" (\"Id\",\"AgentSessionId\",\"Sequence\",\"Kind\",\"CreatedAt\") SELECT gen_random_uuid(),{f.SessionId},n*10,'AssistantText',now() FROM generate_series(1,100) n");
        (await SummaryAsync(db, f.SessionId)).Valid.ShouldBeTrue("S3 folds a valid monotonic append instead of invalidating it");
        (await SummaryAsync(db, f.SessionId)).Count.ShouldBe(101, "gaps are not rows");
        await EqualAsync(db, [f.SessionId]);
        // Duplicate-only SQL must not write the summary at all, including its row version.
        var version = await db.Database.SqlQueryRaw<string>("SELECT xmin::text AS \"Value\" FROM \"SessionTranscriptSummaries\" WHERE \"AgentSessionId\"=@id", new NpgsqlParameter("id", f.SessionId)).SingleAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"TranscriptEntries\" (\"Id\",\"AgentSessionId\",\"Sequence\",\"Kind\",\"CreatedAt\") VALUES ({Guid.NewGuid()},{f.SessionId},1,'AssistantText',now()) ON CONFLICT (\"AgentSessionId\",\"Sequence\") DO NOTHING");
        (await db.Database.SqlQueryRaw<string>("SELECT xmin::text AS \"Value\" FROM \"SessionTranscriptSummaries\" WHERE \"AgentSessionId\"=@id", new NpgsqlParameter("id", f.SessionId)).SingleAsync()).ShouldBe(version);
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await tx.CreateSavepointAsync("c1915");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"TranscriptEntries\" (\"Id\",\"AgentSessionId\",\"Sequence\",\"Kind\",\"CreatedAt\") VALUES ({Guid.NewGuid()},{f.SessionId},2000,'TurnEnd',now())");
            (await SummaryAsync(db, f.SessionId)).Count.ShouldBe(102);
            await tx.RollbackToSavepointAsync("c1915");
            (await SummaryAsync(db, f.SessionId)).Count.ShouldBe(101);
            await tx.CommitAsync();
        }
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"TranscriptEntries\" (\"Id\",\"AgentSessionId\",\"Sequence\",\"Kind\",\"CreatedAt\") VALUES ({Guid.NewGuid()},{f.SessionId},2001,'TurnEnd',now())");
            await tx.RollbackAsync();
        }
        await InstallStatementAuditAsync(db);
        fault.Armed = true;
        var events = scenario == "partial"
            ? new[] { f.Event(1, uuid: "one"), f.Event(2, "TurnEnd", uuid: "poison"), f.Event(3, uuid: "three") }
            : scenario == "stub"
                ? new[] { f.Event(1, uuid: "one"), f.Event(2, "CompactBoundary", text: "(manual)", uuid: "poison") }
                : Enumerable.Range(1, scenario == "normal" ? 100 : 1).Select(n => f.Event(n, scenario == "normal" || scenario == "ef-single" ? "AssistantText" : "TurnEnd", uuid: "ef-" + n)).ToArray();
        var result = await f.Runtime.PersistTranscriptAsync(f.SessionId, events);
        if (scenario == "rollback" || scenario == "ambiguous") fault.Armed.ShouldBeFalse("the real commit cut must have fired");
        var added = scenario == "rollback" ? 0 : scenario is "partial" or "stub" ? 2 : scenario == "normal" ? 100 : 1;
        (await db.TranscriptEntries.LongCountAsync(t => t.AgentSessionId == f.SessionId)).ShouldBe(101 + added);
        var statements = await db.Database.SqlQueryRaw<AppendStatement>("SELECT \"Ordinal\",\"First\",\"Last\",\"Rows\" FROM \"C1915AppendAudit\" WHERE \"AgentSessionId\"=@id", new NpgsqlParameter("id", f.SessionId)).OrderBy(x => x.Ordinal).ToListAsync();
        statements.Sum(x => x.Rows).ShouldBe(added, "audit contains only actual committed INSERT transitions");
        long frontier = 1000;
        var monotonicSql = true;
        foreach (var statement in statements)
        {
            if (statement.First <= frontier) monotonicSql = false;
            frontier = Math.Max(frontier, statement.Last);
        }
        // EF may emit one INSERT per entity in primary-key order. The observed statement order,
        // independently audited in PostgreSQL, determines D-5 validity. Never branch on Valid.
        (await SummaryAsync(db, f.SessionId)).Valid.ShouldBe(monotonicSql);
        Console.WriteLine($"C1915 EF rows={added} statements={statements.Count} monotonic={monotonicSql}");
        if (monotonicSql)
        {
            (await SummaryAsync(db, f.SessionId)).Count.ShouldBe(101 + added);
            await EqualAsync(db, [f.SessionId]);
        }
        if (scenario == "normal")
        {
            result.CommittedRows.Count.ShouldBe(100);
            var beforeReplay = System.Text.Json.JsonSerializer.Serialize(await SummaryAsync(db, f.SessionId));
            var replay = await f.Runtime.PersistTranscriptAsync(f.SessionId, events);
            replay.CommittedRows.ShouldBeEmpty();
            (await db.TranscriptEntries.LongCountAsync(t => t.AgentSessionId == f.SessionId)).ShouldBe(201);
            System.Text.Json.JsonSerializer.Serialize(await SummaryAsync(db, f.SessionId)).ShouldBe(beforeReplay);
            if (monotonicSql) await EqualAsync(db, [f.SessionId]);
        }
        await using var fresh = f.NewContainer();
        var cold = (await fresh.GetRequiredService<ISessionStateLoader>().LoadAsync([f.SessionId], default))[f.SessionId];
        cold.Count.ShouldBe(101 + added);
    }

    [Test]
    public async Task Timestamps_ties_and_end_maxima_remain_independent()
    {
        await using var f = await SessionStateTestFixture.CreateAsync();
        await using var db = f.Db();
        await SessionTranscriptSummaryMigrationFixture.SetMaintenanceAsync(db, true);
        db.SessionTranscriptSummaries.Add(new SessionTranscriptSummary { AgentSessionId = f.SessionId, Valid = true });
        await db.SaveChangesAsync();
        TranscriptEntry Row(long seq, string kind, int? seconds, string? text = null) => new() {
            Id = Guid.NewGuid(), AgentSessionId = f.SessionId, Sequence = seq, Kind = kind, Text = text,
            Timestamp = seconds is {} n ? Epoch.AddSeconds(n) : null, CreatedAt = Epoch.AddDays(1) };
        // Greatest sequence, greatest end timestamp, and greatest effective time are different rows.
        await AppendRowsAsync(db, Row(4, "AssistantText", 100), Row(2, "TurnEnd", 10), Row(1, "TurnEnd", 100), Row(5, "TurnTitle", 500));
        var summary = await SummaryAsync(db, f.SessionId);
        summary.Valid.ShouldBeTrue();
        summary.EndSequence.ShouldBe(2);
        summary.EndTimestamp.ShouldBe(Epoch.AddSeconds(100));
        summary.LastSequence.ShouldBe(5); Working(summary).ShouldBeTrue("equality with the independent end timestamp is working");
        await EqualAsync(db, [f.SessionId]);
        await AppendRowsAsync(db, Row(6, "CompactBoundary", null, "(auto)"), Row(7, "UserPrompt", null, "<command-name>/x"));
        (await SummaryAsync(db, f.SessionId)).HasNullTimestampActivity.ShouldBeFalse();
        await EqualAsync(db, [f.SessionId]);
        await AppendRowsAsync(db, Row(8, "CompactBoundary", null, "(manual)"));
        summary = await SummaryAsync(db, f.SessionId);
        summary.EndSequence.ShouldBe(8); summary.EndTimestamp.ShouldBe(Epoch.AddSeconds(100)); Working(summary).ShouldBeFalse();
        await EqualAsync(db, [f.SessionId]);
        await AppendRowsAsync(db, Row(9, "AssistantText", null));
        Working(await SummaryAsync(db, f.SessionId)).ShouldBeTrue("null activity timestamps cannot prove stale");
        await AppendRowsAsync(db, Row(10, "UserPrompt", 5, "[Request interrupted by user]"), Row(11, "AssistantText", 20));
        Working(await SummaryAsync(db, f.SessionId)).ShouldBeFalse("later sequence does not make an earlier timestamp current");
        await EqualAsync(db, [f.SessionId]);
    }

    [Test]
    public async Task Randomized_multisession_statements_match_the_frozen_seed()
    {
        await using var f = await SessionStateTestFixture.CreateAsync();
        await using var db = f.Db();
        await SessionTranscriptSummaryMigrationFixture.SetMaintenanceAsync(db, true);
        var random = new Random(191503);
        var ids = Enumerable.Range(0, 128).Select(_ => Guid.NewGuid()).ToArray();
        db.AgentSessions.AddRange(ids.Select(id => new AgentSession { Id = id, DefinitionName = "c1915-fold", Cwd = "/synthetic", Status = SessionStatus.Stopped, CreatedAt = Epoch }));
        db.SessionTranscriptSummaries.AddRange(ids.Select(id => new SessionTranscriptSummary { AgentSessionId = id, Valid = true }));
        await db.SaveChangesAsync();
        var kinds = new[] { "AssistantText", "ToolCall", "TurnEnd", "TurnTitle", "UserPrompt", "CompactBoundary", "SessionRestartBoundary", "QueuedUserPrompt", "QueueEnqueue", "QueueDequeue", "QueueRemove" };
        var texts = new string?[] { null, "hello", "(manual)", "(auto)", "[Request interrupted by user]", "<command-name>x", "<local-command-stdout>x", "This session is being continued from a previous conversation" };
        foreach (var generation in Enumerable.Range(0, 3))
        {
            var rows = ids.Skip(1).SelectMany(id => Enumerable.Range(0, random.Next(1, 12)).Select(n => new TranscriptEntry {
                Id = Guid.NewGuid(), AgentSessionId = id, Sequence = generation == 0 && n == 0 ? long.MinValue : generation * 1000 + n * 17 - 31,
                Kind = kinds[random.Next(kinds.Length)], Text = texts[random.Next(texts.Length)],
                Timestamp = random.Next(4) == 0 ? null : Epoch.AddSeconds(random.Next(5)), CreatedAt = Epoch.AddDays(1) })).OrderBy(_ => random.Next()).ToArray();
            // One true multi-session INSERT statement, in shuffled encounter order.
            await db.Database.ExecuteSqlRawAsync("INSERT INTO \"TranscriptEntries\" (\"Id\",\"AgentSessionId\",\"Sequence\",\"Kind\",\"Text\",\"Timestamp\",\"CreatedAt\") SELECT \"Id\",\"AgentSessionId\",\"Sequence\",\"Kind\",\"Text\",\"Timestamp\",\"CreatedAt\" FROM jsonb_populate_recordset(NULL::\"TranscriptEntries\", @rows::jsonb)",
                new NpgsqlParameter("rows", System.Text.Json.JsonSerializer.Serialize(rows)));
            await EqualAsync(db, ids);
        }
        (await SummaryAsync(db, ids[0])).Count.ShouldBe(0);
        // A row below the locked last sequence cannot be folded, even if it is a timestamp maximum.
        db.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = ids[1], Sequence = -100, Kind = "TurnEnd", CreatedAt = Epoch.AddYears(1) });
        await db.SaveChangesAsync();
        (await SummaryAsync(db, ids[1])).Valid.ShouldBeFalse();
    }

    [Test]
    public async Task Opposite_multisession_transactions_abort_atomically_on_deadlock()
    {
        await using var f = await SessionStateTestFixture.CreateAsync();
        await using var db = f.Db();
        await SessionTranscriptSummaryMigrationFixture.SetMaintenanceAsync(db, true);
        var ids = new[] { f.SessionId, f.OtherId };
        db.SessionTranscriptSummaries.AddRange(ids.Select(id => new SessionTranscriptSummary { AgentSessionId = id, Valid = true }));
        await db.SaveChangesAsync();
        await using var left = new NpgsqlConnection(db.Database.GetConnectionString());
        await using var right = new NpgsqlConnection(db.Database.GetConnectionString());
        await using var observer = new NpgsqlConnection(db.Database.GetConnectionString());
        await Task.WhenAll(left.OpenAsync(), right.OpenAsync(), observer.OpenAsync());
        await using var lt = await left.BeginTransactionAsync();
        await using var rt = await right.BeginTransactionAsync();
        await InsertAsync(left, lt, ids[0], 1);
        await InsertAsync(right, rt, ids[1], 1);
        var first = FinishAsync(left, lt, ids[1]);
        try
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                await using var blocked = new NpgsqlCommand("SELECT @owner = ANY(pg_blocking_pids(@waiter))", observer) { CommandTimeout = 10 };
                blocked.Parameters.AddWithValue("owner", right.ProcessID);
                blocked.Parameters.AddWithValue("waiter", left.ProcessID);
                if ((bool)(await blocked.ExecuteScalarAsync(budget.Token))!) break;
                await Task.Yield();
            }
            var second = FinishAsync(right, rt, ids[0]);
            var outcomes = await Task.WhenAll(first, second);
            outcomes.Count(aborted => aborted).ShouldBe(1, "one complete transaction must abort; a partial summary cannot commit");
            await EqualAsync(db, ids);
            (await db.TranscriptEntries.CountAsync(t => ids.Contains(t.AgentSessionId))).ShouldBe(2);
            (await db.SessionTranscriptSummaries.AsNoTracking().Where(s => ids.Contains(s.AgentSessionId)).ToListAsync()).ShouldAllBe(s => s.Count == 1);
        }
        finally
        {
            if (!first.IsCompleted) await rt.RollbackAsync();
            await first;
        }

        static async Task InsertAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, long sequence)
        {
            await using var command = new NpgsqlCommand("INSERT INTO \"TranscriptEntries\" (\"Id\",\"AgentSessionId\",\"Sequence\",\"Kind\",\"CreatedAt\") VALUES (gen_random_uuid(),@id,@seq,'AssistantText',now())", connection, transaction) { CommandTimeout = 10 };
            command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("seq", sequence);
            await command.ExecuteNonQueryAsync();
        }
        static async Task<bool> FinishAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id)
        {
            try { await InsertAsync(connection, transaction, id, 2); await transaction.CommitAsync(); return false; }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.DeadlockDetected)
            { await transaction.RollbackAsync(); return true; }
            catch { await transaction.RollbackAsync(); throw; }
        }
    }

    // These audit objects exist only in this test's owned clone. They observe real EF SQL order;
    // no command is rewritten, no production trigger is replaced and no transcript text is logged.
    private static Task InstallStatementAuditAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE "C1915AppendAudit" ("Ordinal" bigint GENERATED ALWAYS AS IDENTITY,
            "AgentSessionId" uuid NOT NULL, "First" bigint NOT NULL, "Last" bigint NOT NULL, "Rows" bigint NOT NULL);
        CREATE FUNCTION c1915_test_audit() RETURNS trigger LANGUAGE plpgsql AS $audit$
        BEGIN
            INSERT INTO "C1915AppendAudit" ("AgentSessionId","First","Last","Rows")
            SELECT "AgentSessionId", min("Sequence"), max("Sequence"), count(*) FROM audit_entries GROUP BY "AgentSessionId";
            RETURN NULL;
        END;
        $audit$;
        CREATE TRIGGER zz_c1915_test_audit AFTER INSERT ON "TranscriptEntries"
            REFERENCING NEW TABLE AS audit_entries FOR EACH STATEMENT EXECUTE FUNCTION c1915_test_audit();
        """);

    private static Task AppendRowsAsync(AppDbContext db, params TranscriptEntry[] rows) => db.Database.ExecuteSqlRawAsync(
        "INSERT INTO \"TranscriptEntries\" (\"Id\",\"AgentSessionId\",\"Sequence\",\"Kind\",\"Text\",\"Timestamp\",\"CreatedAt\") SELECT \"Id\",\"AgentSessionId\",\"Sequence\",\"Kind\",\"Text\",\"Timestamp\",\"CreatedAt\" FROM jsonb_populate_recordset(NULL::\"TranscriptEntries\", @rows::jsonb)",
        new NpgsqlParameter("rows", System.Text.Json.JsonSerializer.Serialize(rows)));

    private sealed class AppendStatement
    {
        public long Ordinal { get; set; }
        public long First { get; set; }
        public long Last { get; set; }
        public long Rows { get; set; }
    }

    private static DateTime Epoch => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static bool Working(SessionTranscriptSummary s) => new SessionStateSnapshot(s.AgentSessionId) {
        EndSequence = s.EndSequence, EndTimestamp = s.EndTimestamp, HasActivity = s.HasActivity,
        HasNullTimestampActivity = s.HasNullTimestampActivity, ActivityTimestamp = s.ActivityTimestamp }.Working;
    private static Task<SessionTranscriptSummary> SummaryAsync(AppDbContext db, Guid id) => db.SessionTranscriptSummaries.AsNoTracking().SingleAsync(s => s.AgentSessionId == id);

    private static async Task EqualAsync(AppDbContext db, Guid[] ids)
    {
        var actual = await db.Database.SqlQueryRaw<string>("SELECT COALESCE(jsonb_agg(to_jsonb(s) - ARRAY['AgentSessionId','SchemaVersion','Valid','NextBackfillAttemptAt'] ORDER BY s.\"AgentSessionId\"), '[]'::jsonb)::text AS \"Value\" FROM \"SessionTranscriptSummaries\" s WHERE s.\"AgentSessionId\"=ANY(@ids)", new NpgsqlParameter("ids", ids)).SingleAsync();
        var expected = await db.Database.SqlQueryRaw<string>("SELECT COALESCE(jsonb_agg(to_jsonb(r) - ARRAY['SessionId','Exists','Pinned','AcceptedGeneration'] ORDER BY r.\"SessionId\"), '[]'::jsonb)::text AS \"Value\" FROM (" + SessionStateSeedBaseline.Sql + ") r", new NpgsqlParameter("sessionIds", ids)).SingleAsync();
        actual.ShouldBe(expected, "all 15 persisted fields and per-session sequence selections must equal independent frozen SQL");
        (await db.SessionTranscriptSummaries.AsNoTracking().Where(s => ids.Contains(s.AgentSessionId)).ToListAsync()).ShouldAllBe(s => s.Valid && s.SchemaVersion == 1);
    }

    private sealed class PersistenceFault(string scenario) : SaveChangesInterceptor, IDbTransactionInterceptor
    {
        public bool Armed;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var poison = eventData.Context!.ChangeTracker.Entries<TranscriptEntry>().FirstOrDefault(e => e.State == EntityState.Added && e.Entity.Uuid == "poison")?.Entity;
            if (Armed && poison is not null && (scenario == "partial" || scenario == "stub" && poison.Text?.StartsWith(AgentSessionRuntime.TranscriptStubPrefix) != true))
                throw new DbUpdateException("planned row rejection", new PostgresException("planned", "ERROR", "ERROR", "XX000"));
            return ValueTask.FromResult(result);
        }
        public ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Armed && scenario == "rollback") { Armed = false; throw new InvalidOperationException("planned rollback before commit"); }
            return ValueTask.FromResult(result);
        }
        public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Armed && scenario == "ambiguous") { Armed = false; throw new InvalidOperationException("planned ambiguous commit"); }
            return Task.CompletedTask;
        }
    }
}
