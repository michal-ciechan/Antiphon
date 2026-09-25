using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
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
