using System.Data.Common;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
public class SessionStateWarmupTests
{
    [Test]
    public async Task Concurrent_cold_readers_share_one_bounded_statement()
    {
        await using var f = await SessionStateTestFixture.CreateAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            f.Store.ReadBatchAsync([f.SessionId, f.OtherId], default)));
        f.SeedQueries.ShouldBe(1);
        results.ShouldAllBe(r => r.Count == 2 && r[f.SessionId].Readiness == SessionStateReadiness.Ready);
        await f.Store.WarmAsync(default); f.SeedQueries.ShouldBe(1);
        f.Commands.Snapshot()["seed"].ShouldBe(1);
        f.Commands.Snapshot()["pins"].ShouldBe(1);
    }

    [Test]
    public async Task New_container_rebuilds_committed_state_with_a_new_epoch()
    {
        await using var f = await SessionStateTestFixture.CreateAsync();
        await f.Runtime.PersistTranscriptAsync(f.SessionId, [f.Event(1, "UserPrompt"), f.Event(2, "TurnEnd")]);
        var old = await f.Store.ReadAsync(f.SessionId, default);
        await using var restarted = f.NewContainer();
        var store = restarted.GetRequiredService<SessionStateStore>();
        await store.WarmAsync(default);
        var state = await store.ReadAsync(f.SessionId, default);
        state.ServerEpoch.ShouldNotBe(old.ServerEpoch); state.Count.ShouldBe(2); state.Working.ShouldBeFalse();
        state.LastSequence.ShouldBe(old.LastSequence); state.LastEntryId.ShouldBe(old.LastEntryId);
        f.Capture.Clear();
        for (var i = 0; i < 20; i++) await store.ReadAsync(f.SessionId, default);
        f.SeedQueries.ShouldBe(0);
    }

    [Test]
    public async Task Delayed_seed_cannot_overwrite_an_arriving_ingest()
    {
        ControlledLoader? loader = null;
        await using var f = await SessionStateTestFixture.CreateAsync(decorate: inner => loader = new(inner) { Pause = true });
        var seed = f.Store.ReadAsync(f.SessionId, default);
        await loader!.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var ingest = f.Runtime.PersistTranscriptAsync(f.SessionId, [f.Event(1)]);
        try { ingest.IsCompleted.ShouldBeFalse(); loader.Calls.ShouldBe(1, "the arriving ingest must wait for the existing seed"); }
        finally { loader.Release.TrySetResult(); }
        (await seed).Count.ShouldBe(0); (await ingest).LastStoredSeq.ShouldBe(1);
        var state = await f.Store.ReadAsync(f.SessionId, default);
        state.Count.ShouldBe(1); state.Working.ShouldBeTrue(); f.SeedQueries.ShouldBe(1);
    }

    [Test]
    public async Task Cancelled_waiter_does_not_cancel_or_poison_the_shared_load()
    {
        ControlledLoader? loader = null;
        await using var f = await SessionStateTestFixture.CreateAsync(decorate: inner => loader = new(inner) { Pause = true });
        using var caller = new CancellationTokenSource();
        var cancelled = f.Store.ReadAsync(f.SessionId, caller.Token);
        await loader!.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        caller.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => cancelled);
        var surviving = f.Store.ReadAsync(f.SessionId, default);
        loader.Release.TrySetResult();
        (await surviving).Readiness.ShouldBe(SessionStateReadiness.Ready); f.SeedQueries.ShouldBe(1);
    }

    [Test]
    public async Task Failed_load_throttles_from_completion_then_retries_without_caching_idle()
    {
        var clock = new AdvancingClock();
        ControlledLoader? loader = null;
        await using var f = await SessionStateTestFixture.CreateAsync(clock: clock,
            decorate: inner => loader = new(inner) { Pause = true, Fail = true });
        var first = f.Store.ReadAsync(f.SessionId, default);
        await loader!.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        clock.Advance(TimeSpan.FromSeconds(30)); loader.Release.TrySetResult();
        await Should.ThrowAsync<InvalidOperationException>(() => first);
        loader.Fail = false;
        await Should.ThrowAsync<InvalidOperationException>(() => f.Store.ReadAsync(f.SessionId, default));
        loader.Calls.ShouldBe(1);
        clock.Advance(TimeSpan.FromSeconds(6));
        (await f.Store.ReadAsync(f.SessionId, default)).Readiness.ShouldBe(SessionStateReadiness.Ready);
        loader.Calls.ShouldBe(2);
    }

    [Test]
    public async Task Retention_and_session_delete_reseed_before_returning()
    {
        await using var f = await SessionStateTestFixture.CreateAsync();
        await f.Runtime.PersistTranscriptAsync(f.SessionId, [f.Event(1)]);
        await using var db = f.Db();
        var old = DateTime.UtcNow.AddDays(-200);
        await db.AgentSessions.Where(s => s.Id == f.SessionId).ExecuteUpdateAsync(s =>
            s.SetProperty(x => x.Status, SessionStatus.Stopped).SetProperty(x => x.LastSeenAt, old));
        await db.TranscriptEntries.Where(t => t.AgentSessionId == f.SessionId).ExecuteUpdateAsync(t => t.SetProperty(x => x.CreatedAt, old));
        // These setup-only raw edits precede the production mutation under test. Reseed metadata.
        using (var lease = await f.Store.BeginWriteAsync(f.SessionId, default)) await lease.ReconcileAsync(false, default);
        var before = await f.Store.ReadAsync(f.SessionId, default);
        var audit = Options.Create(new AuditSettings());
        var retention = new DataRetentionService(db, Options.Create(new RetentionSettings()), audit,
            TimeProvider.System, NullLogger<DataRetentionService>.Instance, new AuditService(db, audit), f.Store);
        (await retention.PruneTranscriptsAsync(default)).ShouldBe(1);
        var empty = await f.Store.ReadAsync(f.SessionId, default);
        empty.Count.ShouldBe(0); empty.LastSequence.ShouldBe(0); empty.Working.ShouldBeFalse();
        empty.ResetEpoch.ShouldBeGreaterThan(before.ResetEpoch); empty.Readiness.ShouldBe(SessionStateReadiness.Ready);
        (await retention.PruneSessionsAsync(default)).ShouldBe(1);
        var missing = await f.Store.ReadAsync(f.SessionId, default);
        missing.Readiness.ShouldBe(SessionStateReadiness.Missing); missing.ResetEpoch.ShouldBeGreaterThan(empty.ResetEpoch);
    }

    [Test]
    public async Task Terminal_eviction_preserves_active_and_waiting_gates()
    {
        var clock = new AdvancingClock();
        await using var f = await SessionStateTestFixture.CreateAsync(clock: clock,
            settings: new SessionStateSettings { TerminalIdleMinutes = 1 });
        await using var db = f.Db();
        await db.AgentSessions.Where(s => s.Id == f.OtherId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SessionStatus.Stopped));
        await f.Store.ReadBatchAsync([f.SessionId, f.OtherId], default);
        using var held = await f.Store.BeginWriteAsync(f.OtherId, default);
        var waiting = f.Store.ReadAsync(f.OtherId, default);
        clock.Advance(TimeSpan.FromMinutes(2));
        await f.Store.ReadAsync(f.SessionId, default);
        f.Store.CachedCount.ShouldBe(2); waiting.IsCompleted.ShouldBeFalse(); f.SeedQueries.ShouldBe(1);
        held.Dispose(); await waiting;
        clock.Advance(TimeSpan.FromMinutes(2));
        await f.Store.ReadAsync(f.SessionId, default);
        f.Store.CachedCount.ShouldBe(1);
        f.Capture.Clear(); await f.Store.ReadAsync(f.SessionId, default); f.SeedQueries.ShouldBe(0);
        await f.Store.ReadAsync(f.OtherId, default); f.SeedQueries.ShouldBe(1);
    }

    [Test]
    public async Task Capacity_pressure_uses_correct_uncached_reads_without_evicting_active_state()
    {
        await using var f = await SessionStateTestFixture.CreateAsync(settings: new SessionStateSettings { MaxSessions = 1 });
        await f.Runtime.PersistTranscriptAsync(f.SessionId, [f.Event(1)]);
        await f.Runtime.PersistTranscriptAsync(f.OtherId, [f.Event(1) with { SessionId = f.OtherId }]);
        f.Capture.Clear();
        (await f.Store.ReadAsync(f.SessionId, default)).Working.ShouldBeTrue(); f.SeedQueries.ShouldBe(0);
        for (var i = 0; i < 2; i++) (await f.Store.ReadAsync(f.OtherId, default)).Working.ShouldBeTrue();
        f.SeedQueries.ShouldBe(2); f.Store.CachedCount.ShouldBe(1);
    }

    [Test]
    public async Task Rollback_switch_restores_SQL_reads_but_keeps_committed_ingestion()
    {
        await using var f = await SessionStateTestFixture.CreateAsync(settings: new SessionStateSettings { Enabled = false });
        await f.Runtime.PersistTranscriptAsync(f.SessionId, [f.Event(1)]);
        var queue = f.Services.GetRequiredService<SessionMessageQueueService>();
        f.Capture.Clear();
        for (var i = 0; i < 2; i++) (await queue.GetQueueAsync(f.SessionId, default)).Working.ShouldBeTrue();
        f.Capture.Reads.Count(c => c.Sql.Contains("session-state.fallback")).ShouldBe(2);
        f.Commands.Snapshot()["fallback"].ShouldBe(2);
        (await f.Store.ReadAsync(f.SessionId, default)).Count.ShouldBe(1);
        await f.Runtime.WriteRestartBoundaryIfInterruptedAsync(f.SessionId, default);
        (await f.Store.ReadAsync(f.SessionId, default)).Working.ShouldBeFalse();
        (await queue.GetQueueAsync(f.SessionId, default)).Working.ShouldBeFalse();
    }

    [Test]
    public async Task Warmup_pins_a_terminal_session_owing_a_channel_reply()
    {
        var clock = new AdvancingClock();
        await using var f = await SessionStateTestFixture.CreateAsync(clock: clock);
        await using var db = f.Db();
        await db.AgentSessions.Where(s => s.Id == f.OtherId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SessionStatus.Stopped));
        db.SessionQueuedMessages.Add(new Antiphon.Server.Domain.Entities.SessionQueuedMessage
        {
            Id = Guid.NewGuid(), AgentSessionId = f.OtherId, Sequence = 1, Body = "synthetic channel work",
            Status = QueuedMessageStatus.Sent, Origin = QueuedMessageOrigin.Channel,
            DeliveryVerdict = DeliveryVerdict.Delivered, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        await f.Store.WarmAsync(default);
        f.Capture.Clear(); clock.Advance(TimeSpan.FromMinutes(16));
        var state = await f.Store.ReadAsync(f.OtherId, default);
        state.Pinned.ShouldBeTrue(); f.SeedQueries.ShouldBe(0);
    }

    [Test]
    public async Task Committed_board_delete_evicts_the_cached_working_entry()
    {
        var clock = new AdvancingClock();
        await using var f = await SessionStateTestFixture.CreateAsync(clock: clock);
        var graph = await WarmCardSessionAsync(f);
        await using var db = f.Db();
        await Boards(db, f).DeleteAsync(graph.BoardId, default);
        await AssertDeletedSessionStaysMissingAsync(f, clock);
    }

    [Test]
    public async Task Committed_project_delete_evicts_the_cached_working_entry()
    {
        var clock = new AdvancingClock();
        await using var f = await SessionStateTestFixture.CreateAsync(clock: clock);
        var graph = await WarmCardSessionAsync(f);
        await using var db = f.Db();
        await Projects(db, f).DeleteAsync(graph.ProjectId, force: true, default);
        await AssertDeletedSessionStaysMissingAsync(f, clock);
    }

    [Test]
    public async Task Rolled_back_board_delete_leaves_the_cached_snapshot()
    {
        var fault = new CommitFault { Before = true };
        await using var f = await SessionStateTestFixture.CreateAsync(fault);
        var before = await WarmCardSessionAsync(f);
        fault.Armed = true;
        await using var db = f.Db();
        var ex = await Should.ThrowAsync<Exception>(() =>
            Boards(db, f).DeleteAsync(before.BoardId, default));
        ex.ToString().ShouldContain("planned rollback");
        (await f.Store.ReadAsync(f.SessionId, default)).ShouldBe(before.Snapshot);
        await using var verify = f.Db();
        (await verify.AgentSessions.AnyAsync(s => s.Id == f.SessionId)).ShouldBeTrue();
        (await verify.TranscriptEntries.AnyAsync(t => t.AgentSessionId == f.SessionId)).ShouldBeTrue();
    }

    [Test]
    public async Task Ambiguous_project_commit_reloads_instead_of_keeping_working()
    {
        var fault = new CommitFault { After = true };
        await using var f = await SessionStateTestFixture.CreateAsync(fault);
        var graph = await WarmCardSessionAsync(f);
        fault.Armed = true;
        await using var db = f.Db();
        var ex = await Should.ThrowAsync<Exception>(() => Projects(db, f).DeleteAsync(graph.ProjectId, force: true, default));
        ex.ToString().ShouldContain("planned ambiguous commit");
        await using var verify = f.Db();
        (await verify.AgentSessions.AnyAsync(s => s.Id == f.SessionId)).ShouldBeFalse();
        var state = await f.Store.ReadAsync(f.SessionId, default);
        state.Readiness.ShouldBe(SessionStateReadiness.Missing);
        state.Working.ShouldBeFalse();
        state.Count.ShouldBe(0);
    }

    [Test]
    public async Task Cascade_delete_does_not_resurrect_a_committed_ingest()
    {
        var pause = new PauseAfterTranscriptSave();
        await using var f = await SessionStateTestFixture.CreateAsync(pause);
        var graph = await WarmCardSessionAsync(f);
        pause.Armed = true;
        var ingest = f.Runtime.PersistTranscriptAsync(f.SessionId, [f.Event(2, uuid: "race-row")]);
        await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task? deleting = null;
        await using var db = f.Db();
        try
        {
            var wait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Store.NextGateWait = wait;
            deleting = Boards(db, f).DeleteAsync(graph.BoardId, default);
            var winner = await Task.WhenAny(deleting, wait.Task).WaitAsync(TimeSpan.FromSeconds(10));
            f.Store.NextGateWait = null;
            winner.ShouldBe(wait.Task, "cascade delete must take the session gate held by the in-flight ingest");
        }
        finally
        {
            pause.Release.TrySetResult();
        }

        await ingest;
        deleting.ShouldNotBeNull();
        await deleting;
        await using var verify = f.Db();
        (await verify.AgentSessions.AnyAsync(s => s.Id == f.SessionId)).ShouldBeFalse();
        (await verify.TranscriptEntries.AnyAsync(t => t.AgentSessionId == f.SessionId)).ShouldBeFalse();
        var state = await f.Store.ReadAsync(f.SessionId, default);
        state.Readiness.ShouldBe(SessionStateReadiness.Missing);
        state.Working.ShouldBeFalse();
        state.Count.ShouldBe(0);
        (await f.Services.GetRequiredService<SessionMessageQueueService>().GetQueueAsync(f.SessionId, default)).Working.ShouldBeFalse();
    }

    [Test]
    public async Task Retention_prune_drops_cached_working_state()
    {
        var clock = new AdvancingClock();
        await using var f = await SessionStateTestFixture.CreateAsync(clock: clock);
        await f.Runtime.PersistTranscriptAsync(f.SessionId, [f.Event(1)]);
        (await f.Store.ReadAsync(f.SessionId, default)).Working.ShouldBeTrue();
        await using var db = f.Db();
        var old = DateTime.UtcNow.AddDays(-200);
        await db.AgentSessions.Where(s => s.Id == f.SessionId).ExecuteUpdateAsync(s =>
            s.SetProperty(x => x.Status, SessionStatus.Stopped).SetProperty(x => x.LastSeenAt, old));
        await db.TranscriptEntries.Where(t => t.AgentSessionId == f.SessionId)
            .ExecuteUpdateAsync(t => t.SetProperty(x => x.CreatedAt, old));
        var audit = Options.Create(new AuditSettings());
        var retention = new DataRetentionService(db, Options.Create(new RetentionSettings()), audit,
            TimeProvider.System, NullLogger<DataRetentionService>.Instance, new AuditService(db, audit), f.Store);
        (await retention.PruneTranscriptsAsync(default)).ShouldBe(1);
        for (var i = 0; i < 20; i++)
        {
            var empty = await f.Store.ReadAsync(f.SessionId, default);
            empty.Count.ShouldBe(0);
            empty.Working.ShouldBeFalse();
            empty.Readiness.ShouldBe(SessionStateReadiness.Ready);
        }

        clock.Advance(TimeSpan.FromMinutes(2));
        f.Store.CachedCount.ShouldBe(1, "continued reads keep the entry inside the idle window");
        (await f.Store.ReadAsync(f.SessionId, default)).Working.ShouldBeFalse();
        (await retention.PruneSessionsAsync(default)).ShouldBe(1);
        for (var i = 0; i < 20; i++)
        {
            var missing = await f.Store.ReadAsync(f.SessionId, default);
            missing.Readiness.ShouldBe(SessionStateReadiness.Missing);
            missing.Working.ShouldBeFalse();
            missing.Count.ShouldBe(0);
        }
    }

    private static BoardService Boards(AppDbContext db, SessionStateTestFixture f) =>
        new(db, new MockEventBus(), TimeProvider.System, states: f.Store);

    private static ProjectService Projects(AppDbContext db, SessionStateTestFixture f) =>
        new(db, null!, Options.Create(new GithubSettings()), NullLogger<ProjectService>.Instance, states: f.Store);

    private static async Task AssertDeletedSessionStaysMissingAsync(SessionStateTestFixture f, AdvancingClock clock)
    {
        for (var i = 0; i < 20; i++)
        {
            var state = await f.Store.ReadAsync(f.SessionId, default);
            state.Readiness.ShouldBe(SessionStateReadiness.Missing);
            state.Working.ShouldBeFalse();
            state.Count.ShouldBe(0);
        }

        clock.Advance(TimeSpan.FromMinutes(2));
        var still = await f.Store.ReadAsync(f.SessionId, default);
        still.Readiness.ShouldBe(SessionStateReadiness.Missing);
        f.Store.CachedCount.ShouldBe(1, "the missing entry is still inside the idle window, so eviction did not clear it");
        (await f.Services.GetRequiredService<SessionMessageQueueService>().GetQueueAsync(f.SessionId, default)).Working.ShouldBeFalse();
        await using var verify = f.Db();
        (await verify.AgentSessions.AnyAsync(s => s.Id == f.SessionId)).ShouldBeFalse();
    }

    private static async Task<CardGraph> WarmCardSessionAsync(SessionStateTestFixture f)
    {
        await f.Runtime.PersistTranscriptAsync(f.SessionId, [f.Event(1)]);
        var snapshot = await f.Store.ReadAsync(f.SessionId, default);
        snapshot.Working.ShouldBeTrue();
        await using var db = f.Db();
        var now = DateTime.UtcNow;
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        db.Projects.Add(new Project
        {
            Id = projectId,
            Name = "c701-" + projectId.ToString("N"),
            GitRepositoryUrl = "https://example.test/c701.git",
            LocalRepositoryPath = Path.Combine(Path.GetTempPath(), "c701-" + projectId.ToString("N")),
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Boards.Add(new Board
        {
            Id = boardId, ProjectId = projectId, Name = "c701", CreatedAt = now, UpdatedAt = now
        });
        db.BoardColumns.Add(new BoardColumn
        {
            Id = columnId, BoardId = boardId, StateKey = "backlog", Name = "Backlog",
            CardStatus = CardStatus.Backlog, CreatedAt = now, UpdatedAt = now
        });
        db.Cards.Add(new Card
        {
            Id = cardId, BoardId = boardId, BoardColumnId = columnId, Identifier = "CARD-0701",
            Title = "cache", CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
        await db.AgentSessions.Where(s => s.Id == f.SessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CardId, cardId));
        return new CardGraph(projectId, boardId, snapshot);
    }

    private readonly record struct CardGraph(Guid ProjectId, Guid BoardId, SessionStateSnapshot Snapshot);

    private sealed class CommitFault : DbTransactionInterceptor
    {
        public bool Before { get; init; }
        public bool After { get; init; }
        public bool Armed;

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && Before)
            {
                Armed = false;
                throw new InvalidOperationException("planned rollback");
            }

            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Armed && After)
            {
                Armed = false;
                throw new InvalidOperationException("planned ambiguous commit");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class PauseAfterTranscriptSave : SaveChangesInterceptor
    {
        public bool Armed;
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<TranscriptEntry>().Any())
            {
                Armed = false;
                Reached.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }

            return result;
        }
    }

    private sealed class ControlledLoader(ISessionStateLoader inner) : ISessionStateLoader
    {
        public bool Pause { get; init; }
        public bool Fail { get; set; }
        public int Calls;
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IReadOnlyDictionary<Guid, SessionStateSnapshot>> LoadAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            var result = await inner.LoadAsync(ids, ct);
            if (Pause) { Reached.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), ct); }
            if (Fail) throw new InvalidOperationException("planned seed failure");
            return result;
        }
        public Task<IReadOnlySet<Guid>> LoadPinnedIdsAsync(CancellationToken ct) => inner.LoadPinnedIdsAsync(ct);
    }

    private sealed class AdvancingClock : TimeProvider
    {
        private TimeSpan _offset;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _offset;
        public void Advance(TimeSpan by) => _offset += by;
    }
}
