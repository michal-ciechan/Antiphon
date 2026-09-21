using System.Runtime.CompilerServices;
using Antiphon.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Antiphon.DockerStack.Fixture;

public sealed class OpenTransactionException : Exception
{
    public OpenTransactionException() : base("OpenTransaction") { }
}

public sealed class OrdinaryDeliverySaveInterceptor : SaveChangesInterceptor
{
    private readonly ConditionalWeakTable<DbContext, List<EntityStateSnapshot>> _snapshots = new();
    public List<EntityStateSnapshot> Reached { get; } = new();
    public string? FaultUuid { get; set; }
    public bool FaultArmed { get; set; }
    public Func<bool>? Visible { get; set; }
    public int SaveCalls { get; private set; }
    public DeliveryGate? VerdictGate { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            Remember(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            Publish(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        OnSaveFailed(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    public void OnSaveFailed(DbContext? context)
    {
        _ = context;
    }

    public void Remember(DbContext context)
    {
        var snapshots = context.ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .Select(entry => new EntityStateSnapshot(entry.Metadata.Name, entry.State, ReadUuid(entry.Entity), ReadKind(entry.Entity)))
            .ToList();
        FaultSnapshots(snapshots);

        _snapshots.Remove(context);
        _snapshots.Add(context, snapshots);
    }

    public void FaultSnapshots(IReadOnlyList<EntityStateSnapshot> snapshots)
    {
        if (!FaultArmed)
            return;
        var batch = snapshots.Where(snapshot => snapshot.Uuid == FaultUuid).ToList();
        if (batch.Count > 0)
            throw new DbUpdateException("transcript-save-fails");
    }

    public void Publish(DbContext context)
    {
        if (context.Database.CurrentTransaction != null)
            throw new OpenTransactionException();
        if (!_snapshots.TryGetValue(context, out var snapshots))
            return;
        foreach (var snapshot in snapshots)
        {
            if (Visible is not null && !Visible())
                continue;
            Reached.Add(snapshot);
        }
    }

    public async Task HoldVerdictAsync(Func<Task> save, CancellationToken cancellationToken)
    {
        if (VerdictGate is { } gate && gate.IsArmed("receipt-before-verdict"))
            await gate.WaitAsync(cancellationToken);
        SaveCalls++;
        await save();
    }

    private static string? ReadUuid(object entity) =>
        entity is TranscriptEntry transcript ? transcript.Uuid : null;

    private static string? ReadKind(object entity) =>
        entity is TranscriptEntry transcript ? transcript.Kind : null;
}

public sealed record EntityStateSnapshot(string Entity, EntityState State, string? Uuid, string? Kind);
