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
    [Arguments("normal")]
    [Arguments("rollback")]
    [Arguments("ambiguous")]
    [Arguments("partial")]
    [Arguments("stub")]
    public async Task Monotonic_append_folds_only_committed_rows(string scenario)
    {
        var fault = new PersistenceFault(scenario);
        await using var f = await SessionStateTestFixture.CreateAsync(fault);
        await using var db = f.Db();
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
        fault.Armed = true;
        var events = scenario == "partial"
            ? new[] { f.Event(1, uuid: "one"), f.Event(2, "TurnEnd", uuid: "poison"), f.Event(3, uuid: "three") }
            : scenario == "stub"
                ? new[] { f.Event(1, uuid: "one"), f.Event(2, "CompactBoundary", text: "(manual)", uuid: "poison") }
                : Enumerable.Range(1, scenario == "normal" ? 100 : 1).Select(n => f.Event(n, scenario == "normal" ? "AssistantText" : "TurnEnd", uuid: "ef-" + n)).ToArray();
        await f.Runtime.PersistTranscriptAsync(f.SessionId, events);
        if (scenario == "rollback" || scenario == "ambiguous") fault.Armed.ShouldBeFalse("the real commit cut must have fired");
        var added = scenario == "rollback" ? 0 : scenario is "partial" or "stub" ? 2 : scenario == "normal" ? 100 : 1;
        (await SummaryAsync(db, f.SessionId)).Count.ShouldBe(101 + added);
        await EqualAsync(db, [f.SessionId]);
        // Actual runtime replay dedupes UUIDs; it must not increment metadata again.
        if (scenario == "normal")
        {
            await f.Runtime.PersistTranscriptAsync(f.SessionId, events);
            (await SummaryAsync(db, f.SessionId)).Count.ShouldBe(201);
            await EqualAsync(db, [f.SessionId]);
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
        db.SessionTranscriptSummaries.Add(new SessionTranscriptSummary { AgentSessionId = f.SessionId, Valid = true });
        await db.SaveChangesAsync();
        TranscriptEntry Row(long seq, string kind, int? seconds, string? text = null) => new() {
            Id = Guid.NewGuid(), AgentSessionId = f.SessionId, Sequence = seq, Kind = kind, Text = text,
            Timestamp = seconds is {} n ? Epoch.AddSeconds(n) : null, CreatedAt = Epoch.AddDays(1) };
        // Greatest sequence, greatest end timestamp, and greatest effective time are different rows.
        db.TranscriptEntries.AddRange(Row(4, "AssistantText", 100), Row(2, "TurnEnd", 10), Row(1, "TurnEnd", 100), Row(5, "TurnTitle", 500));
        await db.SaveChangesAsync();
        var summary = await SummaryAsync(db, f.SessionId);
        summary.EndSequence.ShouldBe(2); summary.EndTimestamp.ShouldBe(Epoch.AddSeconds(100));
        summary.LastSequence.ShouldBe(5); Working(summary).ShouldBeTrue("equality with the independent end timestamp is working");
        await EqualAsync(db, [f.SessionId]);
        db.TranscriptEntries.AddRange(Row(6, "CompactBoundary", null, "(auto)"), Row(7, "UserPrompt", null, "<command-name>/x"));
        await db.SaveChangesAsync();
        (await SummaryAsync(db, f.SessionId)).HasNullTimestampActivity.ShouldBeFalse();
        await EqualAsync(db, [f.SessionId]);
        db.TranscriptEntries.Add(Row(8, "CompactBoundary", null, "(manual)")); await db.SaveChangesAsync();
        summary = await SummaryAsync(db, f.SessionId);
        summary.EndSequence.ShouldBe(8); summary.EndTimestamp.ShouldBe(Epoch.AddSeconds(100)); Working(summary).ShouldBeFalse();
        await EqualAsync(db, [f.SessionId]);
        db.TranscriptEntries.Add(Row(9, "AssistantText", null)); await db.SaveChangesAsync();
        Working(await SummaryAsync(db, f.SessionId)).ShouldBeTrue("null activity timestamps cannot prove stale");
        db.TranscriptEntries.AddRange(Row(10, "UserPrompt", 5, "[Request interrupted by user]"), Row(11, "AssistantText", 20));
        await db.SaveChangesAsync();
        Working(await SummaryAsync(db, f.SessionId)).ShouldBeFalse("later sequence does not make an earlier timestamp current");
        await EqualAsync(db, [f.SessionId]);
    }

    [Test]
    public async Task Randomized_multisession_statements_match_the_frozen_seed()
    {
        await using var f = await SessionStateTestFixture.CreateAsync();
        await using var db = f.Db();
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
