using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Antiphon.Tests.TestHelpers;

/// <summary>CARD-0502: throw once on SavingChanges or SavedChanges for failure-boundary tests.</summary>
public sealed class ThrowOnceSaveInterceptor : SaveChangesInterceptor
{
    private int _saving;
    private int _saved;
    private bool _targeted;

    /// <param name="when">
    /// Optional target. When given, only a save whose pending changes satisfy it can take the
    /// fault, so an unrelated save (another test's leftover row being corrected in a shared
    /// database) cannot use up the one shot. It is evaluated at SavingChanges, before the
    /// change tracker accepts the changes.
    /// </param>
    public ThrowOnceSaveInterceptor(bool onSaving = true, bool onSaved = false, Func<DbContext, bool>? when = null)
    {
        ThrowOnSaving = onSaving;
        ThrowOnSaved = onSaved;
        When = when;
    }

    public bool ThrowOnSaving { get; }
    public bool ThrowOnSaved { get; }
    public Func<DbContext, bool>? When { get; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        _targeted = When is null || (eventData.Context is { } context && When(context));
        if (ThrowOnSaving && _targeted && Interlocked.Exchange(ref _saving, 1) == 0)
            throw new InvalidOperationException("ThrowOnceSaveInterceptor: SavingChangesAsync");
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (ThrowOnSaved && _targeted && Interlocked.Exchange(ref _saved, 1) == 0)
            throw new InvalidOperationException("ThrowOnceSaveInterceptor: SavedChangesAsync");
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }
}
