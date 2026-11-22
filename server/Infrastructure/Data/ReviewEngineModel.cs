using Antiphon.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Infrastructure.Data;

/// <summary>Review engine tables, first slice (docs/features/012-review-engine/design.md section 18).</summary>
public partial class AppDbContext
{
    public DbSet<ReviewSubject> ReviewSubjects => Set<ReviewSubject>();
    public DbSet<ReviewRun> ReviewRuns => Set<ReviewRun>();
    public DbSet<ReviewNodeExecution> ReviewNodeExecutions => Set<ReviewNodeExecution>();
    public DbSet<ReviewFinding> ReviewFindings => Set<ReviewFinding>();
    public DbSet<ReviewFindingEvent> ReviewFindingEvents => Set<ReviewFindingEvent>();
    public DbSet<ReviewGate> ReviewGates => Set<ReviewGate>();
    public DbSet<ReviewGatePass> ReviewGatePasses => Set<ReviewGatePass>();
    public DbSet<ReviewIteration> ReviewIterations => Set<ReviewIteration>();
    public DbSet<ReviewWorkflowRevision> ReviewWorkflowRevisions => Set<ReviewWorkflowRevision>();

    private static void ConfigureReviewEngineModel(ModelBuilder model)
    {
        model.Entity<ReviewSubject>(e =>
        {
            e.ToTable("ReviewSubjects");
            e.HasKey(s => s.Id);
            e.Property(s => s.Host).HasConversion<string>().HasMaxLength(32);
            e.Property(s => s.Repo).HasMaxLength(256);
            e.Property(s => s.Title).HasMaxLength(1000);
            e.Property(s => s.Url).HasMaxLength(2000);
            e.Property(s => s.AuthorLogin).HasMaxLength(200);
            e.Property(s => s.AuthorDisplayName).HasMaxLength(200);
            e.Property(s => s.AuthorResolvedBy).HasMaxLength(200);
            e.Property(s => s.OpenedByLogin).HasMaxLength(200);
            e.Property(s => s.BaseRef).HasMaxLength(500);
            e.Property(s => s.HeadRef).HasMaxLength(500);
            e.Property(s => s.BaseSha).HasMaxLength(64);
            e.Property(s => s.HeadSha).HasMaxLength(64);
            e.HasIndex(s => new { s.Host, s.Repo, s.PrNumber });
        });

        model.Entity<ReviewRun>(e =>
        {
            e.ToTable("ReviewRuns");
            e.HasKey(r => r.Id);
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(r => r.Stage).HasConversion<string>().HasMaxLength(32);
            e.Property(r => r.WaitingOn).HasConversion<string>().HasMaxLength(32);
            e.Property(r => r.CostUsd).HasPrecision(18, 6);
            e.Property(r => r.WorkflowName).HasMaxLength(200);
            e.Property(r => r.WorkflowHash).HasMaxLength(64);
            e.HasOne(r => r.Subject).WithMany().HasForeignKey(r => r.SubjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(r => r.Iterations).WithOne().HasForeignKey(i => i.RunId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ReviewWorkflowRevision>().WithMany().HasForeignKey(r => r.WorkflowRevisionId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(r => r.Nodes).WithOne().HasForeignKey(n => n.RunId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(r => r.Findings).WithOne().HasForeignKey(f => f.RunId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(r => r.Gates).WithOne().HasForeignKey(g => g.RunId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(r => r.SubjectId);
            e.HasIndex(r => new { r.Status, r.UpdatedAt });
        });

        model.Entity<ReviewNodeExecution>(e =>
        {
            e.ToTable("ReviewNodeExecutions");
            e.HasKey(n => n.Id);
            e.Property(n => n.NodeId).HasMaxLength(200);
            e.Property(n => n.Kind).HasConversion<string>().HasMaxLength(32);
            e.Property(n => n.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(n => n.Transport).HasConversion<string>().HasMaxLength(32);
            e.Property(n => n.Provider).HasMaxLength(200);
            e.Property(n => n.Model).HasMaxLength(200);
            e.Property(n => n.CostUsd).HasPrecision(18, 6);
            e.Property(n => n.CostBasis).HasMaxLength(32);
            e.Property(n => n.InputsHash).HasMaxLength(64);
            e.Property(n => n.ItemKey).HasMaxLength(200);
            e.Property(n => n.Outcome).HasMaxLength(100);
            e.HasIndex(n => new { n.RunId, n.Iteration });
        });

        model.Entity<ReviewFinding>(e =>
        {
            e.ToTable("ReviewFindings");
            e.HasKey(f => f.Id);
            e.Property(f => f.Key).HasMaxLength(32);
            e.Property(f => f.CheckId).HasMaxLength(100);
            e.Property(f => f.Severity).HasConversion<string>().HasMaxLength(32);
            e.Property(f => f.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(f => f.SyncState).HasConversion<string>().HasMaxLength(32);
            e.Property(f => f.FilePath).HasMaxLength(1000);
            e.Property(f => f.Title).HasMaxLength(1000);
            e.Property(f => f.ExternalThreadUrl).HasMaxLength(2000);
            e.HasMany(f => f.Events).WithOne().HasForeignKey(ev => ev.FindingId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(f => new { f.RunId, f.Key }).IsUnique();
            e.HasIndex(f => new { f.RunId, f.Status });
        });

        model.Entity<ReviewFindingEvent>(e =>
        {
            e.ToTable("ReviewFindingEvents");
            e.HasKey(ev => ev.Id);
            e.Property(ev => ev.Action).HasConversion<string>().HasMaxLength(32);
            e.Property(ev => ev.ReasonCategory).HasConversion<string>().HasMaxLength(32);
            e.Property(ev => ev.Scope).HasConversion<string>().HasMaxLength(32);
            e.Property(ev => ev.SyncState).HasConversion<string>().HasMaxLength(32);
            e.Property(ev => ev.ActorLogin).HasMaxLength(200);
            e.Property(ev => ev.ActorDisplayName).HasMaxLength(200);
            e.HasIndex(ev => new { ev.FindingId, ev.CreatedAt });
        });

        model.Entity<ReviewGate>(e =>
        {
            e.ToTable("ReviewGates");
            e.HasKey(g => g.Id);
            e.Property(g => g.Kind).HasConversion<string>().HasMaxLength(32);
            e.Property(g => g.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(g => g.Decision).HasConversion<string>().HasMaxLength(32);
            e.Property(g => g.NodeId).HasMaxLength(200);
            e.Property(g => g.TeamKey).HasMaxLength(200);
            e.Property(g => g.AssigneeLogin).HasMaxLength(200);
            e.Property(g => g.AssigneeDisplayName).HasMaxLength(200);
            e.HasMany(g => g.Passes).WithOne().HasForeignKey(p => p.GateId).OnDelete(DeleteBehavior.Cascade);
            e.Property(g => g.DecidedBy).HasMaxLength(200);
            e.HasIndex(g => new { g.Status, g.OpenedAt });
            e.HasIndex(g => g.RunId);
        });

        model.Entity<ReviewGatePass>(e =>
        {
            e.ToTable("ReviewGatePasses");
            e.HasKey(p => p.Id);
            e.Property(p => p.Login).HasMaxLength(200);
            e.Property(p => p.Reason).HasMaxLength(2000);
            e.HasIndex(p => new { p.GateId, p.At });
        });

        model.Entity<ReviewIteration>(e =>
        {
            e.ToTable("ReviewIterations");
            e.HasKey(i => i.Id);
            e.Property(i => i.Trigger).HasMaxLength(64);
            e.HasIndex(i => new { i.RunId, i.Iteration }).IsUnique();
        });

        model.Entity<ReviewWorkflowRevision>(e =>
        {
            e.ToTable("ReviewWorkflowRevisions");
            e.HasKey(w => w.Id);
            e.Property(w => w.Name).HasMaxLength(200);
            e.Property(w => w.Hash).HasMaxLength(64);
            e.Property(w => w.GraphJson).HasColumnType("jsonb");
            e.HasIndex(w => w.Hash).IsUnique();
        });
    }
}
