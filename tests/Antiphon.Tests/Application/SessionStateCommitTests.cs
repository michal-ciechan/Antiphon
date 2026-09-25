using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
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
public class SessionStateCommitTests
{
    [Test]
    public async Task Readers_wait_for_commit_and_then_observe_the_published_revision()
    {
        var fault = new SaveGate();
        await using var f = await SessionStateTestFixture.CreateAsync(fault);
        var before = await f.Store.ReadAsync(f.SessionId, default);
        var ingest = f.Runtime.PersistTranscriptAsync(f.SessionId, [f.Event(1)]);
        await fault.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var reader = f.Store.ReadAsync(f.SessionId, default);
        try
        {
            await Task.Delay(30);
            reader.IsCompleted.ShouldBeFalse("a racing read must not see the old Ready value during a write");
            await using var db = f.Db();
            (await db.TranscriptEntries.CountAsync()).ShouldBe(0);
        }
        finally { fault.Release.TrySetResult(); }
        (await ingest).LastStoredSeq.ShouldBe(1);
        var after = await reader;
        after.Working.ShouldBeTrue(); after.Count.ShouldBe(1); after.Revision.ShouldBeGreaterThan(before.Revision);
        (await f.Store.ReadAsync(f.SessionId, default)).ShouldBe(after);
    }

    [Test]
    public async Task Failed_save_does_not_advance_snapshot_or_revision()
    {
        var fault = new SaveFault { ThrowBefore = true };
        await using var f = await SessionStateTestFixture.CreateAsync(fault);
        var before = await f.Store.ReadAsync(f.SessionId, default);
        (await f.Runtime.PersistTranscriptAsync(f.SessionId, [f.Event(1)])).LastStoredSeq.ShouldBeNull();
        (await f.Store.ReadAsync(f.SessionId, default)).ShouldBe(before);
        (await f.RowsAsync()).ShouldBeEmpty();
        f.Runtime.TryGetTranscriptPersistFailure(f.SessionId, out _).ShouldBeTrue();
    }

    [Test]
    public async Task Partial_fallback_folds_only_successful_rows_once()
    {
        await using var f = await SessionStateTestFixture.CreateAsync(new RejectRow { Uuid = "poison", RejectStub = true });
        var result = await f.Runtime.PersistTranscriptAsync(f.SessionId,
            [f.Event(1, uuid: "one"), f.Event(2, "TurnEnd", uuid: "poison"), f.Event(3, uuid: "three")]);
        result.LastStoredSeq.ShouldBe(3);
        var state = await f.Store.ReadAsync(f.SessionId, default);
        state.Count.ShouldBe(2); state.EndSequence.ShouldBeNull(); state.Working.ShouldBeTrue();
        result.CommittedRows.Count.ShouldBe(2);
        await AssertDurableAsync(f);
    }

    [Test]
    public async Task Persist_stub_uses_stored_classification_and_preserves_source_callback_flags()
    {
        await using var f = await SessionStateTestFixture.CreateAsync(new RejectRow { Uuid = "poison" });
        var result = await f.Runtime.PersistTranscriptAsync(f.SessionId,
            [f.Event(1), f.Event(2, "CompactBoundary", text: "(manual)", uuid: "poison")]);
        result.AddedManualCompactBoundary.ShouldBeTrue("existing source-event semantics remain separate");
        var state = await f.Store.ReadAsync(f.SessionId, default);
        state.Count.ShouldBe(2); state.Working.ShouldBeTrue("the STORED stub contains no manual marker");
        state.EndSequence.ShouldBeNull();
        await AssertDurableAsync(f);
    }

    [Test]
    public async Task Unique_collision_recovers_actual_durable_row_and_sequence()
    {
        var race = new RacingRow();
        await using var f = await SessionStateTestFixture.CreateAsync(race);
        var result = await f.Runtime.PersistTranscriptAsync(f.SessionId,
            [f.Event(1, "UserPrompt", text: "[Request interrupted", uuid: "race")]);
        result.LastStoredSeq.ShouldBe(17, "the proposed sequence 1 never committed");
        var state = await f.Store.ReadAsync(f.SessionId, default);
        state.LastSequence.ShouldBe(17); state.Count.ShouldBe(2);
        state.Working.ShouldBeTrue("the competing durable prompt has ordinary text");
        await AssertDurableAsync(f);
    }

    [Test]
    public async Task Ambiguous_post_commit_exception_reloads_durable_state()
    {
        await using var f = await SessionStateTestFixture.CreateAsync(new SaveFault { ThrowAfter = true });
        var before = await f.Store.ReadAsync(f.SessionId, default);
        await f.Runtime.PersistTranscriptAsync(f.SessionId, [f.Event(1)]);
        var state = await f.Store.ReadAsync(f.SessionId, default);
        state.Count.ShouldBe(1); state.Working.ShouldBeTrue(); state.Revision.ShouldBeGreaterThan(before.Revision);
        await AssertDurableAsync(f);
    }

    [Test]
    public async Task Synthetic_restart_boundary_uses_the_commit_gate_and_is_idempotent()
    {
        await using var f = await SessionStateTestFixture.CreateAsync();
        await f.Runtime.PersistTranscriptAsync(f.SessionId, [f.Event(1)]);
        var before = await f.Store.ReadAsync(f.SessionId, default);
        var nextGeneration = before.AcceptedGeneration!.Value.AddSeconds(1);
        await using (var db = f.Db())
            await db.AgentSessions.Where(s => s.Id == f.SessionId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.StartedAt, nextGeneration));
        (await f.Runtime.WriteRestartBoundaryIfInterruptedAsync(f.SessionId, default)).ShouldBeTrue();
        var after = await f.Store.ReadAsync(f.SessionId, default);
        after.Working.ShouldBeFalse(); after.LastKind.ShouldBe("SessionRestartBoundary");
        after.Count.ShouldBe(2); after.LastSequence.ShouldBe(2); after.Revision.ShouldBeGreaterThan(before.Revision);
        after.AcceptedGeneration.ShouldBe(nextGeneration);
        System.Text.Json.JsonSerializer.SerializeToElement(f.Store.GetMetrics()).GetProperty("ingestCalls").GetInt64().ShouldBe(2);
        (await f.Runtime.WriteRestartBoundaryIfInterruptedAsync(f.SessionId, default)).ShouldBeFalse();
        (await f.Store.ReadAsync(f.SessionId, default)).ShouldBe(after);
        await AssertDurableAsync(f);
    }

    [Test]
    public async Task Concurrent_ingests_rebase_sequences_and_warmed_queue_observes_each_commit()
    {
        await using var f = await SessionStateTestFixture.CreateAsync();
        await Task.WhenAll(Enumerable.Range(0, 12).Select(i =>
            f.Runtime.PersistTranscriptAsync(f.SessionId, [f.Event(1, uuid: $"row-{i}")])));
        var state = await f.Store.ReadAsync(f.SessionId, default);
        state.Count.ShouldBe(12); state.LastSequence.ShouldBe(12);
        var queue = f.Services.GetRequiredService<SessionMessageQueueService>();
        (await queue.GetQueueAsync(f.SessionId, default)).Working.ShouldBeTrue();
        await f.Runtime.PersistTranscriptAsync(f.SessionId, [f.Event(1, "TurnEnd")]);
        (await queue.GetQueueAsync(f.SessionId, default)).Working.ShouldBeFalse();
        var ended = await f.Store.ReadAsync(f.SessionId, default);
        var replay = await f.Runtime.PersistTranscriptAsync(f.SessionId, [f.Event(1, uuid: "row-0")]);
        replay.LastStoredSeq.ShouldBeNull(); (await f.Store.ReadAsync(f.SessionId, default)).ShouldBe(ended);
        var metrics = System.Text.Json.JsonSerializer.SerializeToElement(f.Store.GetMetrics());
        metrics.GetProperty("ingestCalls").GetInt64().ShouldBe(14);
        metrics.GetProperty("committedRows").GetInt64().ShouldBe(13);
        metrics.GetProperty("duplicateBatches").GetInt64().ShouldBe(1);
        await AssertDurableAsync(f);
    }

    private static async Task AssertDurableAsync(SessionStateTestFixture f)
    {
        var state = await f.Store.ReadAsync(f.SessionId, default);
        await using var db = f.Db();
        state.Working.ShouldBe((await TranscriptWorkingStateOracle.ReadAsync(db, [f.SessionId], default))[f.SessionId]);
        state.Count.ShouldBe(await db.TranscriptEntries.LongCountAsync(t => t.AgentSessionId == f.SessionId));
    }

    private sealed class SaveGate : SaveChangesInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<TranscriptEntry>().Any())
            { Reached.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken); }
            return result;
        }
    }

    private sealed class SaveFault : SaveChangesInterceptor
    {
        public bool ThrowBefore { get; init; }
        public bool ThrowAfter { get; init; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (ThrowBefore && eventData.Context!.ChangeTracker.Entries<TranscriptEntry>().Any()) throw new InvalidOperationException("planned before commit");
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (ThrowAfter && eventData.Context!.ChangeTracker.Entries<TranscriptEntry>().Any()) throw new InvalidOperationException("planned lost commit result");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RejectRow : SaveChangesInterceptor
    {
        public required string Uuid { get; init; }
        public bool RejectStub { get; init; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var row = eventData.Context!.ChangeTracker.Entries<TranscriptEntry>()
                .FirstOrDefault(e => e.State == EntityState.Added && e.Entity.Uuid == Uuid)?.Entity;
            if (row is not null && (RejectStub || row.Text?.StartsWith(AgentSessionRuntime.TranscriptStubPrefix) != true))
                throw new DbUpdateException("planned row rejection", new PostgresException("planned", "ERROR", "ERROR", "XX000"));
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RacingRow : SaveChangesInterceptor
    {
        private bool _inserted;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var row = eventData.Context!.ChangeTracker.Entries<TranscriptEntry>().FirstOrDefault()?.Entity;
            if (_inserted || row is null) return result;
            _inserted = true;
            // Same real DB, independent context and transaction. No interception recursion.
            await using var other = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(eventData.Context.Database.GetConnectionString()).Options);
            other.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = row.AgentSessionId,
                Kind = row.Kind, Uuid = row.Uuid, Sequence = 17, Text = "durable ordinary prompt", CreatedAt = DateTime.UtcNow });
            other.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = row.AgentSessionId,
                Kind = "TurnEnd", Uuid = "other", Sequence = row.Sequence, CreatedAt = DateTime.UtcNow });
            await other.SaveChangesAsync(cancellationToken);
            return result;
        }
    }
}
