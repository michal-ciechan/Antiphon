using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Infrastructure.Data;

public partial class AppDbContext
{
    public DbSet<HostLoadSampleRecord> HostLoadSamples => Set<HostLoadSampleRecord>();
    public DbSet<HostLoadSampleIdentity> HostLoadSampleIdentities => Set<HostLoadSampleIdentity>();
    public DbSet<HostLoadHourlyRollup> HostLoadHourlyRollups => Set<HostLoadHourlyRollup>();
    public DbSet<HostLoadIngestCursor> HostLoadIngestCursors => Set<HostLoadIngestCursor>();
    public DbSet<AgentTaskLoadWindow> AgentTaskLoadWindows => Set<AgentTaskLoadWindow>();
    public DbSet<HostLoadTaskMetricBucket> HostLoadTaskMetricBuckets => Set<HostLoadTaskMetricBucket>();
    public DbSet<HostLoadObservedEvent> HostLoadObservedEvents => Set<HostLoadObservedEvent>();

    private static void ConfigureHostLoadModel(ModelBuilder model)
    {
        model.Entity<HostLoadSampleRecord>(e =>
        {
            e.HasKey(r => r.Id); e.Property(r => r.Id).ValueGeneratedOnAdd();
            e.Property(r => r.CreatedTransactionId).HasDefaultValueSql("pg_current_xact_id()::text::bigint");
            e.Property(r => r.RunnerId).HasMaxLength(64); e.Property(r => r.PhysicalHostKey).HasMaxLength(128);
            e.HasIndex(r => new { r.RunnerStoreId, r.BootId, r.Sequence }).IsUnique();
            e.HasIndex(r => new { r.PhysicalHostKey, r.SampledAt, r.Id });
            e.HasIndex(r => r.Id).HasFilter("\"DerivedAt\" IS NULL").HasDatabaseName("IX_HostLoadSamples_PendingDerived");
        });
        model.Entity<HostLoadSampleIdentity>(e =>
        {
            e.HasKey(r => new { r.RunnerStoreId, r.BootId, r.Sequence });
            e.Property(r => r.PayloadHash).HasMaxLength(64); e.HasIndex(r => r.IngestedAt);
        });
        model.Entity<HostLoadHourlyRollup>(e =>
        {
            e.HasKey(r => r.Id); e.Property(r => r.Id).ValueGeneratedOnAdd();
            e.Property(r => r.CreatedTransactionId).HasDefaultValueSql("pg_current_xact_id()::text::bigint");
            e.Property(r => r.PhysicalHostKey).HasMaxLength(128); e.Property(r => r.Metric).HasMaxLength(128);
            e.HasIndex(r => new { r.PhysicalHostKey, r.SourceEpoch, r.HourUtc, r.Metric }).IsUnique();
            e.HasIndex(r => new { r.PhysicalHostKey, r.HourUtc, r.Id });
        });
        model.Entity<HostLoadIngestCursor>(e => { e.HasKey(r => r.RunnerId); e.Property(r => r.RunnerId).HasMaxLength(64); });
        model.Entity<AgentTaskLoadWindow>(e =>
        {
            e.HasKey(r => r.Id); e.Property(r => r.PhysicalHostKey).HasMaxLength(128);
            e.HasIndex(r => new { r.TaskId, r.Attempt, r.PhysicalHostKey, r.RunnerBootId, r.WindowOrdinal }).IsUnique();
            e.HasIndex(r => new { r.PhysicalHostKey, r.StartedAt, r.EndedAt });
            e.HasIndex(r => r.EndedAt);
        });
        model.Entity<HostLoadTaskMetricBucket>(e =>
        {
            e.HasKey(r => new { r.WindowId, r.HourUtc, r.Metric }); e.Property(r => r.Metric).HasMaxLength(128);
            e.HasOne<AgentTaskLoadWindow>().WithMany().HasForeignKey(r => r.WindowId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<HostLoadObservedEvent>(e => { e.HasKey(r => r.EventId); e.HasIndex(r => r.ObservedAt); });
        model.Entity<AgentTaskEvent>().HasIndex(e => new { e.At, e.Id }).HasFilter("\"LoadAttempt\" IS NOT NULL")
            .HasDatabaseName("IX_AgentTaskEvents_LoadObservation");
    }

    // Snapshot existing tracked facts at the authoritative event insert. No query, extra save,
    // dispatcher ordering change or asynchronous notification is required for durability.
    private void SnapshotHostLoadEvents()
    {
        var tasks = ChangeTracker.Entries<AgentTask>().ToDictionary(e => e.Entity.Id, e => e.Entity);
        foreach (var entry in ChangeTracker.Entries<AgentTaskEvent>().Where(e => e.State == EntityState.Added))
        {
            var row = entry.Entity;
            if (!tasks.TryGetValue(row.AgentTaskId, out var task)) continue;
            row.LoadAttempt ??= task.Attempt;
            row.LoadRunnerId ??= string.IsNullOrWhiteSpace(task.RunnerId) || task.RunnerId == "local" ? "desktop" : task.RunnerId;
            row.LoadSessionId ??= row.AgentSessionId ?? task.AgentSessionId;
            row.LoadQueuedAt ??= task.Attempt == 1 ? task.CreatedAt : row.Type == AgentTaskEventType.Retried ? row.At : null;
            row.LoadDispatchedAt ??= task.DispatchedAt;
            row.LoadStatus ??= task.Status;
        }
    }
}
