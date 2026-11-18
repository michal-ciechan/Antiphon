using System.Data.Common;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0679 D-3: output activity is a heartbeat, not a ledger. A burst of output chunks writes
/// <c>LastSeenAt</c> once per <c>AgentSession:ActivityWriteMinIntervalMs</c>, not once per chunk.
/// </summary>
[Category("Integration")]
public class AgentSessionRuntimeActivityTests
{
    [Test]
    public async Task Output_bursts_write_LastSeenAt_at_most_once_per_interval()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
        var budget = new QueryBudget(null, "rt.activity.write");
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            TimeProvider = clock,
            ConfigureDbContext = o => o.AddInterceptors(budget),
        });
        // The harness seeds LastSeenAt from this clock. Step past that instant so the first
        // claimed write is a new stamp, then keep the burst inside the one-second claim.
        clock.Advance(TimeSpan.FromSeconds(5));

        var burstStart = clock.GetUtcNow();
        using var activity = budget.Begin();
        for (var sequence = 1; sequence <= 20; sequence++)
        {
            await h.Runtime.ObserveOutputAsync(h.SessionId, sequence, $"chunk-{sequence}", CancellationToken.None);
            clock.Advance(TimeSpan.FromMilliseconds(10));
        }

        clock.SetUtcNow(burstStart + TimeSpan.FromMilliseconds(1001));
        await h.Runtime.ObserveOutputAsync(h.SessionId, 21, "chunk-21", CancellationToken.None);

        activity.Count.ShouldBe(2);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var lastSeen = await db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == h.SessionId)
            .Select(s => s.LastSeenAt)
            .SingleAsync();
        lastSeen.ShouldBe(clock.GetUtcNow().UtcDateTime);
    }

    [Test]
    public async Task C1987_activity_write_is_one_command()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        var budget = new QueryBudget();
        var transactions = new TransactionStarts();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            TimeProvider = clock,
            ConfigureDbContext = o => o.AddInterceptors(budget, transactions),
        });
        await PhoneHomeStrandedQueueTests.ClaimCardAsync(h, schema.ConnectionString);
        clock.Advance(TimeSpan.FromSeconds(5));
        transactions.Reset();
        using var activity = budget.Begin();
        await h.Runtime.ObserveOutputAsync(h.SessionId, 1, "chunk-1", CancellationToken.None);
        var detail = $"{activity.Count} commands, budget 1; transactions {transactions.Begins}, budget 0";
        activity.Count.ShouldBe(1, detail);
        transactions.Begins.ShouldBe(0, detail);
    }

    [Test]
    public async Task C1987_activity_write_sets_both_timestamps()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            TimeProvider = clock,
        });
        var unchanged = clock.GetUtcNow().UtcDateTime;
        var seeded = await SeedAttemptsAsync(h, schema.ConnectionString, unchanged);
        clock.Advance(TimeSpan.FromSeconds(5));
        var written = clock.GetUtcNow().UtcDateTime;
        await h.Runtime.ObserveOutputAsync(h.SessionId, 1, "chunk-1", CancellationToken.None);

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var lastSeen = await db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == h.SessionId)
            .Select(s => s.LastSeenAt)
            .SingleAsync();
        lastSeen.ShouldBe(written);
        var attempts = await db.RunAttempts.AsNoTracking()
            .Where(a => a.AgentSessionId == h.SessionId)
            .Select(a => new { a.Id, a.LastEventAt, a.CompletedAt })
            .ToListAsync();
        attempts.Single(a => a.Id == seeded.NewestStreamingId).LastEventAt.ShouldBe(written);
        attempts.Single(a => a.Id == seeded.OlderStreamingId).LastEventAt.ShouldBe(unchanged);
        var completed = attempts.Single(a => a.Id == seeded.CompletedId);
        completed.LastEventAt.ShouldBe(unchanged);
        completed.CompletedAt.ShouldBe(unchanged);
    }

    private static async Task<SeededAttempts> SeedAttemptsAsync(
        BridgeQueueHarness harness, string connectionString, DateTime unchanged)
    {
        var (cardId, olderId) = await PhoneHomeStrandedQueueTests.ClaimCardAsync(harness, connectionString);
        var newestId = Guid.NewGuid();
        var completedId = Guid.NewGuid();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        var older = await db.RunAttempts.SingleAsync(a => a.Id == olderId);
        older.LastEventAt = unchanged;
        older.CompletedAt = null;
        older.Phase = RunPhase.StreamingTurn;
        db.RunAttempts.Add(Attempt(newestId, cardId, harness.SessionId, 2, RunPhase.StreamingTurn, unchanged, completedAt: null));
        db.RunAttempts.Add(Attempt(completedId, cardId, harness.SessionId, 3, RunPhase.Succeeded, unchanged, completedAt: unchanged));
        await db.SaveChangesAsync();
        return new SeededAttempts(olderId, newestId, completedId);
    }

    private static RunAttempt Attempt(
        Guid id, Guid cardId, Guid sessionId, int number, RunPhase phase, DateTime at, DateTime? completedAt) =>
        new()
        {
            Id = id,
            CardId = cardId,
            AgentSessionId = sessionId,
            AttemptNumber = number,
            Phase = phase,
            CreatedAt = at,
            StartedAt = at,
            LastEventAt = at,
            PhaseStartedAt = at,
            CompletedAt = completedAt,
            Prompt = "card-1987 activity",
        };

    private readonly record struct SeededAttempts(Guid OlderStreamingId, Guid NewestStreamingId, Guid CompletedId);

    private sealed class TransactionStarts : DbTransactionInterceptor
    {
        private int _begins;

        public int Begins => Volatile.Read(ref _begins);

        public void Reset() => Interlocked.Exchange(ref _begins, 0);

        public override InterceptionResult<DbTransaction> TransactionStarting(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result)
        {
            Interlocked.Increment(ref _begins);
            return result;
        }

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _begins);
            return ValueTask.FromResult(result);
        }
    }
}
