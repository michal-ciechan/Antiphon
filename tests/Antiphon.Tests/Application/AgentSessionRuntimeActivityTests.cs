using Antiphon.Server.Domain.Entities;
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
        var writes = new LastSeenWrites();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            TimeProvider = clock,
            ConfigureDbContext = o => o.AddInterceptors(writes),
        });
        writes.Reset();

        // 10 ms apart, so every chunk would be a real change to LastSeenAt (a same-instant rewrite
        // is an EF no-op and could not show the difference); the burst spans 190 ms.
        var burstStart = clock.GetUtcNow();
        for (var sequence = 1; sequence <= 20; sequence++)
        {
            await h.Runtime.ObserveOutputAsync(h.SessionId, sequence, $"chunk-{sequence}", CancellationToken.None);
            clock.Advance(TimeSpan.FromMilliseconds(10));
        }

        clock.SetUtcNow(burstStart + TimeSpan.FromMilliseconds(1001));
        await h.Runtime.ObserveOutputAsync(h.SessionId, 21, "chunk-21", CancellationToken.None);

        writes.Count.ShouldBe(2);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var lastSeen = await db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == h.SessionId)
            .Select(s => s.LastSeenAt)
            .SingleAsync();
        lastSeen.ShouldBe(clock.GetUtcNow().UtcDateTime);
    }

    /// <summary>Counts saves that write an <see cref="AgentSession.LastSeenAt"/> change.</summary>
    private sealed class LastSeenWrites : SaveChangesInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Reset() => Interlocked.Exchange(ref _count, 0);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } context
                && context.ChangeTracker.Entries<AgentSession>().Any(e =>
                    e.State == EntityState.Modified && e.Property(s => s.LastSeenAt).IsModified))
                Interlocked.Increment(ref _count);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
