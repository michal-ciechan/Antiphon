using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Antiphon.Tests.TestHelpers;

/// <summary>CARD-0502: throw once on SavingChanges or SavedChanges for failure-boundary tests.</summary>
public sealed class ThrowOnceSaveInterceptor : SaveChangesInterceptor
{
    private int _saving;
    private int _saved;

    public ThrowOnceSaveInterceptor(bool onSaving = true, bool onSaved = false)
    {
        ThrowOnSaving = onSaving;
        ThrowOnSaved = onSaved;
    }

    public bool ThrowOnSaving { get; }
    public bool ThrowOnSaved { get; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (ThrowOnSaving && Interlocked.Exchange(ref _saving, 1) == 0)
            throw new InvalidOperationException("ThrowOnceSaveInterceptor: SavingChangesAsync");
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (ThrowOnSaved && Interlocked.Exchange(ref _saved, 1) == 0)
            throw new InvalidOperationException("ThrowOnceSaveInterceptor: SavedChangesAsync");
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }
}
