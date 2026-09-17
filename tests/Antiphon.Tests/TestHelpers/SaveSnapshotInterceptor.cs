using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0547 G-547-11d: snapshots the <c>ChangeTracker</c> of every <c>SaveChanges</c> a context
/// performs, so a test can assert that two writes went in TOGETHER.
///
/// <para><see cref="ThrowOnceSaveInterceptor"/> answers "what survives a failed save"; this one
/// answers "which rows shared a save", which is the only thing that can see a change that merely
/// moves an <c>Add</c> across a <c>SaveChangesAsync</c> boundary. CARD-0547's round-2 Review proved
/// by experiment that moving the <c>CommitRecoveryAbandoned</c> adds back AFTER
/// <c>FailAsync</c> — silently reintroducing the round-1 defect — left every ordinary assertion
/// green, because both saves happen within one sweep and the rows are all present at the end.</para>
/// </summary>
public sealed class SaveSnapshotInterceptor : SaveChangesInterceptor
{
    private readonly List<IReadOnlyList<Entry>> _saves = [];

    /// <summary>One entry per tracked row at the moment the save was issued.</summary>
    public sealed record Entry(object Entity, EntityState State);

    /// <summary>One element per <c>SaveChanges</c>, oldest first. Snapshot-safe to enumerate.</summary>
    public IReadOnlyList<IReadOnlyList<Entry>> Saves
    {
        get { lock (_saves) return _saves.ToArray(); }
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Capture(DbContextEventData eventData)
    {
        if (eventData.Context is null)
            return;
        var rows = eventData.Context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => new Entry(e.Entity, e.State))
            .ToArray();
        lock (_saves)
            _saves.Add(rows);
    }
}
