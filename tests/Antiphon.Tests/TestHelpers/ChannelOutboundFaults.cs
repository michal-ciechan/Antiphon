using System.Data.Common;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Antiphon.Tests.TestHelpers;

internal sealed class ChannelOutboundFaults : ChannelOutboundBoundary
{
    private string? _armed;
    private readonly HashSet<string> _reached = [];
    private Guid? _publicationId;
    private string? _pauseBoundary;
    private int _pauseTaken;
    private TaskCompletionSource<bool>? _paused;
    private TaskCompletionSource<bool>? _release;

    public DbSaveFaultInterceptor SaveInterceptor { get; }
    public DbCommandFaultInterceptor CommandInterceptor { get; }
    public FaultingAttachmentReader AttachmentReader { get; }

    public ChannelOutboundFaults()
    {
        SaveInterceptor = new DbSaveFaultInterceptor(this);
        CommandInterceptor = new DbCommandFaultInterceptor(this);
        AttachmentReader = new FaultingAttachmentReader(this);
    }

    public void Arm(string stage)
    {
        _armed = stage;
        _reached.Clear();
        _publicationId = null;
    }
    public void Disarm() => _armed = null;
    public bool WasReached(string stage) => _reached.Contains(stage);

    public void PauseOnceAt(string boundary)
    {
        _pauseBoundary = boundary;
        _pauseTaken = 0;
        _paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public Task WaitPausedAsync() => (_paused?.Task
        ?? throw new InvalidOperationException("No pause was armed."))
        .WaitAsync(TimeSpan.FromSeconds(30));
    public void ReleasePause() => _release?.TrySetResult(true);

    public override async Task ReachAsync(string boundary, Guid publicationId, CancellationToken ct)
    {
        if (boundary == "publication-committed")
            _publicationId = publicationId;
        _reached.Add(boundary);
        if (boundary == _pauseBoundary && Interlocked.CompareExchange(ref _pauseTaken, 1, 0) == 0)
        {
            _paused!.TrySetResult(true);
            await _release!.Task.WaitAsync(ct);
        }
    }

    private bool IsArmed(string stage) => _armed == stage;
    private void Fire(string stage)
    {
        _reached.Add(stage);
        throw new IOException($"C519 fault: {stage}");
    }

    internal sealed class DbSaveFaultInterceptor(ChannelOutboundFaults owner) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not { } db)
                return base.SavingChangesAsync(eventData, result, cancellationToken);
            var publications = db.ChangeTracker.Entries<ChannelOutboundPublication>().ToList();
            if (owner.IsArmed("publication-commit")
                && publications.Any(e => e.State == EntityState.Added))
                owner.Fire("publication-commit");
            if (owner.IsArmed("failure-save")
                && publications.Any(e => e.State == EntityState.Modified
                    && (e.Entity.State == "Unknown" || e.Entity.State == "Held")))
                owner.Fire("failure-save");
            if (owner.IsArmed("outcome-commit")
                && publications.Any(e => e.State == EntityState.Modified
                    && e.Entity.State == "Published" && e.Entity.MetadataStampedAt == null))
                owner.Fire("outcome-commit");
            if (owner.IsArmed("incident-save")
                && db.ChangeTracker.Entries<AgentIncident>().Any(e => e.State == EntityState.Added))
                owner.Fire("incident-save");
            if (owner.IsArmed("bundle-stamp")
                && db.ChangeTracker.Entries<AgentTask>().Any(e => e.State == EntityState.Modified
                    && e.Entity.DeliverableDeliveredAt != null))
                owner.Fire("bundle-stamp");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    internal sealed class DbCommandFaultInterceptor(ChannelOutboundFaults owner) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (owner.IsArmed("attempt-commit") && owner._publicationId is Guid id
                && command.CommandText.Contains("ChannelOutboundPublications", StringComparison.Ordinal)
                && command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
                && command.Parameters.Cast<DbParameter>().Any(p => Equals(p.Value, id)))
                owner.Fire("attempt-commit");
            if (owner.IsArmed("catalog-stamp")
                && command.CommandText.Contains("ChatChannels", StringComparison.Ordinal)
                && command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase))
                owner.Fire("catalog-stamp");
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    internal sealed class FaultingAttachmentReader(ChannelOutboundFaults owner) : ChannelAttachmentReader
    {
        public override byte[] ReadAllBytes(string path)
        {
            if (owner.IsArmed("prepare"))
                owner.Fire("prepare");
            return base.ReadAllBytes(path);
        }
    }
}
