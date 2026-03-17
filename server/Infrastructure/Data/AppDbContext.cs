using Antiphon.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<WorkflowTemplate> WorkflowTemplates => Set<WorkflowTemplate>();
    public DbSet<LlmProvider> LlmProviders => Set<LlmProvider>();
    public DbSet<ModelRouting> ModelRoutings => Set<ModelRouting>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Workflow> Workflows => Set<Workflow>();
    public DbSet<Stage> Stages => Set<Stage>();
    public DbSet<GateDecision> GateDecisions => Set<GateDecision>();
    public DbSet<StageExecution> StageExecutions => Set<StageExecution>();
    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();
    public DbSet<CostLedgerEntry> CostLedgerEntries => Set<CostLedgerEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Enable JSONB column support for flexible config storage
        // Individual entity configurations will use .HasColumnType("jsonb") on properties

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.UserName).IsRequired().HasMaxLength(100);
            entity.Property(u => u.Email).IsRequired().HasMaxLength(256);
            entity.Property(u => u.IsAdmin).IsRequired();
            entity.Property(u => u.CreatedAt).IsRequired();

            entity.HasIndex(u => u.UserName).IsUnique();
            entity.HasIndex(u => u.Email).IsUnique();

            // Seed default admin user (must match CurrentUserMiddleware.DefaultAdminId)
            entity.HasData(new User
            {
                Id = new Guid("a0000000-0000-0000-0000-000000000001"),
                UserName = "admin",
                Email = "admin@antiphon.local",
                IsAdmin = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        });

        modelBuilder.Entity<WorkflowTemplate>(entity =>
        {
            entity.ToTable("WorkflowTemplates");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Name).IsRequired().HasMaxLength(200);
            entity.Property(t => t.Description).HasMaxLength(2000);
            entity.Property(t => t.YamlDefinition).IsRequired();
            entity.Property(t => t.IsBuiltIn).IsRequired();
            entity.Property(t => t.CreatedAt).IsRequired();
            entity.Property(t => t.UpdatedAt).IsRequired();

            entity.HasIndex(t => t.Name).IsUnique();
        });

        modelBuilder.Entity<LlmProvider>(entity =>
        {
            entity.ToTable("LlmProviders");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).IsRequired().HasMaxLength(200);
            entity.Property(p => p.ProviderType).IsRequired();
            entity.Property(p => p.ApiKey).HasMaxLength(500);
            entity.Property(p => p.BaseUrl).HasMaxLength(500);
            entity.Property(p => p.IsEnabled).IsRequired();
            entity.Property(p => p.DefaultModel).HasMaxLength(200);
            entity.Property(p => p.CreatedAt).IsRequired();
            entity.Property(p => p.UpdatedAt).IsRequired();

            entity.HasIndex(p => p.Name).IsUnique();

            entity.HasMany(p => p.ModelRoutings)
                .WithOne(r => r.Provider)
                .HasForeignKey(r => r.ProviderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ModelRouting>(entity =>
        {
            entity.ToTable("ModelRoutings");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.StageName).IsRequired().HasMaxLength(200);
            entity.Property(r => r.ModelName).IsRequired().HasMaxLength(200);
            entity.Property(r => r.ProviderId).IsRequired();
            entity.Property(r => r.CreatedAt).IsRequired();

            entity.HasIndex(r => r.StageName).IsUnique();
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.ToTable("Projects");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).IsRequired().HasMaxLength(200);
            entity.Property(p => p.GitRepositoryUrl).IsRequired().HasMaxLength(500);
            entity.Property(p => p.ConstitutionPath).IsRequired().HasMaxLength(500)
                .HasDefaultValue("project-context.md");
            entity.Property(p => p.GitHubIntegrationEnabled).IsRequired();
            entity.Property(p => p.NotificationsEnabled).IsRequired();
            entity.Property(p => p.CreatedAt).IsRequired();
            entity.Property(p => p.UpdatedAt).IsRequired();

            entity.HasIndex(p => p.Name).IsUnique();
        });

        modelBuilder.Entity<Workflow>(entity =>
        {
            entity.ToTable("Workflows");
            entity.HasKey(w => w.Id);
            entity.Property(w => w.Name).IsRequired().HasMaxLength(200);
            entity.Property(w => w.Description).HasMaxLength(2000);
            entity.Property(w => w.TemplateId).IsRequired();
            entity.Property(w => w.ProjectId).IsRequired();
            entity.Property(w => w.Status).IsRequired();
            entity.Property(w => w.InitialContext).IsRequired();
            entity.Property(w => w.GitBranchName).IsRequired().HasMaxLength(500);
            entity.Property(w => w.CreatedAt).IsRequired();
            entity.Property(w => w.UpdatedAt).IsRequired();

            entity.HasIndex(w => w.ProjectId).HasDatabaseName("IX_Workflows_ProjectId");
            entity.HasIndex(w => w.TemplateId).HasDatabaseName("IX_Workflows_TemplateId");
            entity.HasIndex(w => w.Status).HasDatabaseName("IX_Workflows_Status");

            entity.HasOne(w => w.Template)
                .WithMany()
                .HasForeignKey(w => w.TemplateId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(w => w.Project)
                .WithMany()
                .HasForeignKey(w => w.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(w => w.CurrentStage)
                .WithMany()
                .HasForeignKey(w => w.CurrentStageId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Stage>(entity =>
        {
            entity.ToTable("Stages");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.WorkflowId).IsRequired();
            entity.Property(s => s.Name).IsRequired().HasMaxLength(200);
            entity.Property(s => s.Description).HasMaxLength(2000);
            entity.Property(s => s.StageOrder).IsRequired();
            entity.Property(s => s.Status).IsRequired();
            entity.Property(s => s.ExecutorType).IsRequired().HasMaxLength(100);
            entity.Property(s => s.ModelName).HasMaxLength(200);
            entity.Property(s => s.GateRequired).IsRequired();
            entity.Property(s => s.CurrentVersion).IsRequired().HasDefaultValue(1);
            entity.Property(s => s.CreatedAt).IsRequired();

            entity.HasIndex(s => s.WorkflowId).HasDatabaseName("IX_Stages_WorkflowId");
            entity.HasIndex(s => new { s.WorkflowId, s.StageOrder })
                .IsUnique()
                .HasDatabaseName("IX_Stages_WorkflowId_StageOrder");

            entity.HasOne(s => s.Workflow)
                .WithMany(w => w.Stages)
                .HasForeignKey(s => s.WorkflowId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GateDecision>(entity =>
        {
            entity.ToTable("GateDecisions");
            entity.HasKey(g => g.Id);
            entity.Property(g => g.StageId).IsRequired();
            entity.Property(g => g.WorkflowId).IsRequired();
            entity.Property(g => g.Action).IsRequired();
            entity.Property(g => g.Feedback).HasMaxLength(4000);
            entity.Property(g => g.DecidedBy).IsRequired();
            entity.Property(g => g.CreatedAt).IsRequired();

            entity.HasIndex(g => g.StageId).HasDatabaseName("IX_GateDecisions_StageId");
            entity.HasIndex(g => g.WorkflowId).HasDatabaseName("IX_GateDecisions_WorkflowId");

            entity.HasOne(g => g.Stage)
                .WithMany(s => s.GateDecisions)
                .HasForeignKey(g => g.StageId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(g => g.Workflow)
                .WithMany(w => w.GateDecisions)
                .HasForeignKey(g => g.WorkflowId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(g => g.DecidedByUser)
                .WithMany()
                .HasForeignKey(g => g.DecidedBy)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StageExecution>(entity =>
        {
            entity.ToTable("StageExecutions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.StageId).IsRequired();
            entity.Property(e => e.WorkflowId).IsRequired();
            entity.Property(e => e.Version).IsRequired();
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.StartedAt).IsRequired();
            entity.Property(e => e.ErrorDetails).HasMaxLength(4000);
            entity.Property(e => e.GitTagName).HasMaxLength(500);
            entity.Property(e => e.TokensIn).IsRequired();
            entity.Property(e => e.TokensOut).IsRequired();
            entity.Property(e => e.EstimatedCostUsd).IsRequired().HasPrecision(18, 6);

            entity.HasIndex(e => e.StageId).HasDatabaseName("IX_StageExecutions_StageId");
            entity.HasIndex(e => e.WorkflowId).HasDatabaseName("IX_StageExecutions_WorkflowId");
            entity.HasIndex(e => new { e.StageId, e.Version })
                .IsUnique()
                .HasDatabaseName("IX_StageExecutions_StageId_Version");

            entity.HasOne(e => e.Stage)
                .WithMany(s => s.StageExecutions)
                .HasForeignKey(e => e.StageId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Workflow)
                .WithMany(w => w.StageExecutions)
                .HasForeignKey(e => e.WorkflowId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AuditRecord>(entity =>
        {
            entity.ToTable("AuditRecords");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.EventType).IsRequired();
            entity.Property(a => a.ModelName).HasMaxLength(200);
            entity.Property(a => a.TokensIn).IsRequired();
            entity.Property(a => a.TokensOut).IsRequired();
            entity.Property(a => a.CostUsd).IsRequired().HasPrecision(18, 6);
            entity.Property(a => a.DurationMs).IsRequired();
            entity.Property(a => a.ClientIp).HasMaxLength(100);
            entity.Property(a => a.GitTagName).HasMaxLength(500);
            entity.Property(a => a.Summary).IsRequired().HasMaxLength(2000);
            entity.Property(a => a.FullContent).HasColumnType("jsonb");
            entity.Property(a => a.CreatedAt).IsRequired();

            entity.HasIndex(a => a.WorkflowId).HasDatabaseName("IX_AuditRecords_WorkflowId");
            entity.HasIndex(a => a.StageId).HasDatabaseName("IX_AuditRecords_StageId");
            entity.HasIndex(a => a.CreatedAt).HasDatabaseName("IX_AuditRecords_CreatedAt");
            entity.HasIndex(a => a.EventType).HasDatabaseName("IX_AuditRecords_EventType");

            entity.HasOne(a => a.Workflow)
                .WithMany()
                .HasForeignKey(a => a.WorkflowId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(a => a.Stage)
                .WithMany()
                .HasForeignKey(a => a.StageId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(a => a.StageExecution)
                .WithMany()
                .HasForeignKey(a => a.StageExecutionId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CostLedgerEntry>(entity =>
        {
            entity.ToTable("CostLedgerEntries");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.WorkflowId).IsRequired();
            entity.Property(c => c.StageId).IsRequired();
            entity.Property(c => c.ModelName).IsRequired().HasMaxLength(200);
            entity.Property(c => c.TokensIn).IsRequired();
            entity.Property(c => c.TokensOut).IsRequired();
            entity.Property(c => c.CostUsd).IsRequired().HasPrecision(18, 6);
            entity.Property(c => c.DurationMs).IsRequired();
            entity.Property(c => c.CreatedAt).IsRequired();

            entity.HasIndex(c => c.WorkflowId).HasDatabaseName("IX_CostLedgerEntries_WorkflowId");
            entity.HasIndex(c => c.StageId).HasDatabaseName("IX_CostLedgerEntries_StageId");
            entity.HasIndex(c => c.CreatedAt).HasDatabaseName("IX_CostLedgerEntries_CreatedAt");

            entity.HasOne(c => c.Workflow)
                .WithMany()
                .HasForeignKey(c => c.WorkflowId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(c => c.Stage)
                .WithMany()
                .HasForeignKey(c => c.StageId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(c => c.StageExecution)
                .WithMany()
                .HasForeignKey(c => c.StageExecutionId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(c => c.AuditRecord)
                .WithMany()
                .HasForeignKey(c => c.AuditRecordId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
