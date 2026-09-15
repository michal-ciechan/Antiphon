using Antiphon.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Antiphon.Tests.TestHelpers;

/// <summary>CARD-0527 F3. Fail the queue-row insert while leaving the settlement save intact.</summary>
public sealed class FailSessionQueuedMessageInsertInterceptor : SaveChangesInterceptor
{
    private int _remaining;

    public FailSessionQueuedMessageInsertInterceptor(int times = 1) => _remaining = times;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ThrowIfQueuedInsert(eventData);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ThrowIfQueuedInsert(eventData);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ThrowIfQueuedInsert(DbContextEventData eventData)
    {
        if (eventData.Context is null)
            return;
        if (!eventData.Context.ChangeTracker.Entries<SessionQueuedMessage>()
                .Any(e => e.State == EntityState.Added))
            return;
        if (System.Threading.Interlocked.Decrement(ref _remaining) < 0)
            return;
        throw new InvalidOperationException("FailSessionQueuedMessageInsertInterceptor: queue insert");
    }
}
