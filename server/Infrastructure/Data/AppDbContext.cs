using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
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
    public DbSet<TemplateGroup> TemplateGroups => Set<TemplateGroup>();
    public DbSet<Board> Boards => Set<Board>();
    public DbSet<BoardColumn> BoardColumns => Set<BoardColumn>();
    public DbSet<Card> Cards => Set<Card>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<AgentBundleAttachment> AgentBundleAttachments => Set<AgentBundleAttachment>();
    public DbSet<CardRevision> CardRevisions => Set<CardRevision>();
    public DbSet<CardWorkflowRun> CardWorkflowRuns => Set<CardWorkflowRun>();
    public DbSet<CardWorkflowStage> CardWorkflowStages => Set<CardWorkflowStage>();
    public DbSet<AgentSession> AgentSessions => Set<AgentSession>();
    public DbSet<TranscriptEntry> TranscriptEntries => Set<TranscriptEntry>();
    public DbSet<ApiErrorRecovery> ApiErrorRecoveries => Set<ApiErrorRecovery>();
    public DbSet<SessionQueuedMessage> SessionQueuedMessages => Set<SessionQueuedMessage>();
    public DbSet<RunAttempt> RunAttempts => Set<RunAttempt>();
    public DbSet<Worktree> Worktrees => Set<Worktree>();
    public DbSet<BoardWorkflowDefinition> BoardWorkflowDefinitions => Set<BoardWorkflowDefinition>();
    public DbSet<ExternalIssueRef> ExternalIssueRefs => Set<ExternalIssueRef>();
    public DbSet<RetrySchedule> RetrySchedules => Set<RetrySchedule>();
    public DbSet<TokenUsage> TokenUsages => Set<TokenUsage>();
    public DbSet<ArtifactSectionReview> ArtifactSectionReviews => Set<ArtifactSectionReview>();
    public DbSet<ChatChannel> ChatChannels => Set<ChatChannel>();
    public DbSet<AgentSupervisionState> AgentSupervisionStates => Set<AgentSupervisionState>();
    public DbSet<AgentIncident> AgentIncidents => Set<AgentIncident>();
    public DbSet<FileReviewState> FileReviewStates => Set<FileReviewState>();
    public DbSet<FileSectionReview> FileSectionReviews => Set<FileSectionReview>();
    public DbSet<AgentReviewCheckpoint> AgentReviewCheckpoints => Set<AgentReviewCheckpoint>();
    public DbSet<ReviewThread> ReviewThreads => Set<ReviewThread>();
    public DbSet<ReviewComment> ReviewComments => Set<ReviewComment>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<AgentTask> AgentTasks => Set<AgentTask>();
    public DbSet<AgentTaskEvent> AgentTaskEvents => Set<AgentTaskEvent>();
    public DbSet<AgentTuiProfile> AgentTuiProfiles => Set<AgentTuiProfile>();
    public DbSet<AgentTuiProfileRevision> AgentTuiProfileRevisions => Set<AgentTuiProfileRevision>();
    public DbSet<AgentTuiSecret> AgentTuiSecrets => Set<AgentTuiSecret>();
    public DbSet<AgentTuiModel> AgentTuiModels => Set<AgentTuiModel>();
    public DbSet<AgentTuiValidationRun> AgentTuiValidationRuns => Set<AgentTuiValidationRun>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<FileReviewState>(entity =>
        {
            entity.ToTable("FileReviewStates");
            entity.HasKey(f => f.Id);
            entity.Property(f => f.Path).IsRequired().HasMaxLength(1024);
            entity.Property(f => f.ContentHash).IsRequired().HasMaxLength(64);
            entity.Property(f => f.UpdatedAt).IsRequired();
            entity.HasIndex(f => new { f.AgentId, f.Path }).IsUnique()
                .HasDatabaseName("IX_FileReviewStates_AgentId_Path");
            entity.HasOne(f => f.Agent)
                .WithMany()
                .HasForeignKey(f => f.AgentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FileSectionReview>(entity =>
        {
            entity.ToTable("FileSectionReviews");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Path).IsRequired().HasMaxLength(1024);
            entity.Property(s => s.SectionKey).IsRequired().HasMaxLength(300);
            entity.Property(s => s.ContentHash).IsRequired().HasMaxLength(64);
            entity.Property(s => s.UpdatedAt).IsRequired();
            entity.HasIndex(s => new { s.AgentId, s.Path, s.SectionKey }).IsUnique()
                .HasDatabaseName("IX_FileSectionReviews_AgentId_Path_SectionKey");
            entity.HasOne(s => s.Agent)
                .WithMany()
                .HasForeignKey(s => s.AgentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentReviewCheckpoint>(entity =>
        {
            entity.ToTable("AgentReviewCheckpoints");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.CommitSha).HasMaxLength(64);
            entity.Property(c => c.Reason).IsRequired().HasMaxLength(500);
            entity.Property(c => c.CreatedAt).IsRequired();
            entity.HasIndex(c => new { c.AgentId, c.CreatedAt })
                .HasDatabaseName("IX_AgentReviewCheckpoints_AgentId_CreatedAt");
            entity.HasOne(c => c.Agent)
                .WithMany()
                .HasForeignKey(c => c.AgentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReviewThread>(entity =>
        {
            entity.ToTable("ReviewThreads");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Path).IsRequired().HasMaxLength(1024);
            entity.Property(t => t.Snippet).HasMaxLength(2000);
            entity.Property(t => t.CreatedAt).IsRequired();
            entity.Property(t => t.UpdatedAt).IsRequired();
            entity.HasIndex(t => new { t.AgentId, t.Status }).HasDatabaseName("IX_ReviewThreads_AgentId_Status");
            entity.HasIndex(t => new { t.AgentId, t.Path }).HasDatabaseName("IX_ReviewThreads_AgentId_Path");
            entity.HasOne(t => t.Agent)
                .WithMany()
                .HasForeignKey(t => t.AgentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReviewComment>(entity =>
        {
            entity.ToTable("ReviewComments");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Body).IsRequired();
            entity.Property(c => c.CreatedAt).IsRequired();
            entity.HasIndex(c => new { c.ThreadId, c.CreatedAt }).HasDatabaseName("IX_ReviewComments_ThreadId_CreatedAt");
            entity.HasOne(c => c.Thread)
                .WithMany(t => t.Comments)
                .HasForeignKey(c => c.ThreadId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentSupervisionState>(entity =>
        {
            entity.ToTable("AgentSupervisionStates");
            entity.HasKey(s => s.AgentId);
            entity.Property(s => s.UpdatedAt).IsRequired();

            entity.HasOne(s => s.Agent)
                .WithOne()
                .HasForeignKey<AgentSupervisionState>(s => s.AgentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Alert>(entity =>
        {
            entity.ToTable("Alerts");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Source).IsRequired().HasMaxLength(50);
            entity.Property(a => a.Title).IsRequired().HasMaxLength(500);
            entity.Property(a => a.Detail).HasMaxLength(4000);
            entity.Property(a => a.DedupKey).IsRequired().HasMaxLength(500);
            entity.Property(a => a.CreatedAt).IsRequired();

            entity.HasIndex(a => a.CreatedAt).HasDatabaseName("IX_Alerts_CreatedAt");
            entity.HasIndex(a => new { a.DedupKey, a.CreatedAt }).HasDatabaseName("IX_Alerts_DedupKey_CreatedAt");
        });

        modelBuilder.Entity<AgentIncident>(entity =>
        {
            entity.ToTable("AgentIncidents");
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Message).IsRequired().HasMaxLength(4000);
            entity.Property(i => i.FailureReason).HasMaxLength(4000);
            entity.Property(i => i.CreatedAt).IsRequired();

            entity.HasIndex(i => new { i.AgentId, i.CreatedAt })
                .HasDatabaseName("IX_AgentIncidents_AgentId_CreatedAt");

            entity.HasOne(i => i.Agent)
                .WithMany()
                .HasForeignKey(i => i.AgentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChatChannel>(entity =>
        {
            entity.ToTable("ChatChannels");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Provider).IsRequired().HasMaxLength(50);
            entity.Property(c => c.ExternalId).IsRequired().HasMaxLength(200);
            entity.Property(c => c.Title).HasMaxLength(500);
            entity.Property(c => c.ReplyHandle).HasMaxLength(500);
            entity.Property(c => c.LastMessagePreview).HasMaxLength(500);
            entity.Property(c => c.LastAuthor).HasMaxLength(200);
            entity.Property(c => c.CreatedAt).IsRequired();
            entity.Property(c => c.UpdatedAt).IsRequired();

            // Upsert key: one row per provider conversation.
            entity.HasIndex(c => new { c.Provider, c.ExternalId }).IsUnique();

            entity.HasOne(c => c.Agent)
                .WithMany()
                .HasForeignKey(c => c.AgentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

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

        modelBuilder.Entity<TemplateGroup>(entity =>
        {
            entity.ToTable("TemplateGroups");
            entity.HasKey(g => g.Id);
            entity.Property(g => g.Name).IsRequired().HasMaxLength(200);
            entity.Property(g => g.Description).IsRequired().HasMaxLength(2000);
            entity.Property(g => g.IsBuiltIn).IsRequired();
            entity.Property(g => g.CreatedAt).IsRequired();
            entity.Property(g => g.UpdatedAt).IsRequired();

            entity.HasIndex(g => g.Name).IsUnique().HasDatabaseName("IX_TemplateGroups_Name");
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

            entity.HasOne(t => t.TemplateGroup)
                .WithMany(g => g.Templates)
                .HasForeignKey(t => t.TemplateGroupId)
                .OnDelete(DeleteBehavior.SetNull);
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
            entity.Property(r => r.WorkflowTemplateId);

            // Composite unique: same stage name can exist for different templates
            entity.HasIndex(r => new { r.WorkflowTemplateId, r.StageName })
                .IsUnique()
                .HasDatabaseName("IX_ModelRoutings_WorkflowTemplateId_StageName");

            entity.HasOne(r => r.WorkflowTemplate)
                .WithMany()
                .HasForeignKey(r => r.WorkflowTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.ToTable("Projects");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).IsRequired().HasMaxLength(200);
            entity.Property(p => p.GitRepositoryUrl).IsRequired().HasMaxLength(500);
            entity.Property(p => p.LocalRepositoryPath).HasMaxLength(1000);
            entity.Property(p => p.BaseBranch).IsRequired().HasMaxLength(200).HasDefaultValue("master");
            entity.Property(p => p.ConstitutionPath).IsRequired().HasMaxLength(500)
                .HasDefaultValue("AGENTS.md;CLAUDE.md;README.md");
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
            entity.Property(w => w.FeatureName).HasMaxLength(200);
            entity.Property(w => w.GitBranchName).IsRequired().HasMaxLength(500);
            entity.Property(w => w.CreatedAt).IsRequired();
            entity.Property(w => w.UpdatedAt).IsRequired();

            entity.HasIndex(w => w.ProjectId).HasDatabaseName("IX_Workflows_ProjectId");
            entity.HasIndex(w => w.TemplateId).HasDatabaseName("IX_Workflows_TemplateId");
            entity.HasIndex(w => w.Status).HasDatabaseName("IX_Workflows_Status");
            entity.HasIndex(w => new { w.ProjectId, w.FeatureName })
                .HasDatabaseName("IX_Workflows_ProjectId_FeatureName");

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

        modelBuilder.Entity<Board>(entity =>
        {
            entity.ToTable("Boards");
            entity.HasKey(b => b.Id);
            entity.Property(b => b.ProjectId).IsRequired();
            entity.Property(b => b.Name).IsRequired().HasMaxLength(200);
            entity.Property(b => b.Description).HasMaxLength(2000);
            entity.Property(b => b.TrackerKind).IsRequired();
            entity.Property(b => b.MaxConcurrentSessions).IsRequired();
            entity.Property(b => b.CreatedAt).IsRequired();
            entity.Property(b => b.UpdatedAt).IsRequired();

            entity.HasIndex(b => b.ProjectId).HasDatabaseName("IX_Boards_ProjectId");
            entity.HasIndex(b => new { b.ProjectId, b.Name })
                .IsUnique()
                .HasDatabaseName("IX_Boards_ProjectId_Name");

            entity.HasOne(b => b.Project)
                .WithMany(p => p.Boards)
                .HasForeignKey(b => b.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BoardColumn>(entity =>
        {
            entity.ToTable("BoardColumns");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.BoardId).IsRequired();
            entity.Property(c => c.StateKey).IsRequired().HasMaxLength(100);
            entity.Property(c => c.Name).IsRequired().HasMaxLength(200);
            entity.Property(c => c.ColumnOrder).IsRequired();
            entity.Property(c => c.CardStatus).IsRequired();
            entity.Property(c => c.IsActive).IsRequired();
            entity.Property(c => c.IsTerminal).IsRequired();
            entity.Property(c => c.MaxConcurrentSessions);
            entity.Property(c => c.CreatedAt).IsRequired();
            entity.Property(c => c.UpdatedAt).IsRequired();

            entity.HasIndex(c => new { c.BoardId, c.StateKey })
                .IsUnique()
                .HasDatabaseName("IX_BoardColumns_BoardId_StateKey");
            entity.HasIndex(c => new { c.BoardId, c.ColumnOrder })
                .IsUnique()
                .HasDatabaseName("IX_BoardColumns_BoardId_ColumnOrder");

            entity.HasOne(c => c.Board)
                .WithMany(b => b.Columns)
                .HasForeignKey(c => c.BoardId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentTuiProfile>(entity =>
        {
            entity.ToTable("AgentTuiProfiles", table =>
                table.HasCheckConstraint(
                    "CK_AgentTuiProfiles_Kind_Valid",
                    "\"Kind\" IN (0, 1, 2, 3, 4)"));
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).ValueGeneratedNever();
            entity.Property(p => p.DisplayName).IsRequired().HasMaxLength(200);
            entity.Property(p => p.Kind).IsRequired();
            entity.Property(p => p.IsEnabled).IsRequired();
            entity.Property(p => p.IsDefault).IsRequired();
            entity.Property(p => p.Source).IsRequired();
            entity.Property(p => p.SourceDefinitionName).HasMaxLength(200);
            entity.Property(p => p.CreatedAt).IsRequired();
            entity.Property(p => p.UpdatedAt).IsRequired();

            entity.HasIndex(p => p.DisplayName)
                .IsUnique()
                .HasDatabaseName("IX_AgentTuiProfiles_DisplayName");

            entity.HasOne(p => p.ActiveRevision)
                .WithMany()
                .HasForeignKey(p => new { p.Id, p.ActiveRevisionId })
                .HasPrincipalKey(r => new { r.ProfileId, r.Id })
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AgentTuiProfileRevision>(entity =>
        {
            entity.ToTable("AgentTuiProfileRevisions", table =>
            {
                table.HasCheckConstraint(
                    "CK_AgentTuiProfileRevisions_RevisionNumber_Positive",
                    "\"RevisionNumber\" > 0");
                table.HasCheckConstraint(
                    "CK_AgentTuiProfileRevisions_AuthenticationMode_Valid",
                    "\"AuthenticationMode\" IN (0, 1)");
            });
            entity.HasKey(r => r.Id);
            entity.HasAlternateKey(r => new { r.ProfileId, r.Id })
                .HasName("AK_AgentTuiProfileRevisions_ProfileId_Id");
            entity.Property(r => r.ProfileId).IsRequired();
            entity.Property(r => r.RevisionNumber).IsRequired();
            entity.Property(r => r.Executable).IsRequired().HasMaxLength(2000);
            entity.Property(r => r.ArgumentsJson).IsRequired().HasColumnType("jsonb");
            entity.Property(r => r.DiscoveryArgumentsJson).IsRequired().HasColumnType("jsonb");
            entity.Property(r => r.VersionArgumentsJson).IsRequired().HasColumnType("jsonb");
            entity.Property(r => r.WorkingDirectory).HasMaxLength(1000);
            entity.Property(r => r.AuthenticationMode).IsRequired();
            entity.Property(r => r.NonSecretEnvironmentJson).IsRequired().HasColumnType("jsonb");
            entity.Property(r => r.SecretEnvironmentNamesJson).IsRequired().HasColumnType("jsonb");
            entity.Property(r => r.ModelArgumentName).HasMaxLength(100);
            entity.Property(r => r.Guidance).IsRequired().HasMaxLength(4000);
            entity.Property(r => r.CreatedAt).IsRequired();

            entity.HasIndex(r => new { r.ProfileId, r.RevisionNumber })
                .IsUnique()
                .HasDatabaseName("IX_AgentTuiProfileRevisions_ProfileId_RevisionNumber");

            entity.HasOne(r => r.Profile)
                .WithMany(p => p.Revisions)
                .HasForeignKey(r => r.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentTuiSecret>(entity =>
        {
            entity.ToTable("AgentTuiSecrets");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.ProfileId).IsRequired();
            entity.Property(s => s.Name).IsRequired().HasMaxLength(200);
            entity.Property(s => s.Ciphertext).IsRequired();
            entity.Property(s => s.ProtectionVersion).IsRequired().HasMaxLength(100);
            entity.Property(s => s.CreatedAt).IsRequired();
            entity.Property(s => s.UpdatedAt).IsRequired();

            entity.HasIndex(s => new { s.ProfileId, s.Name })
                .IsUnique()
                .HasDatabaseName("IX_AgentTuiSecrets_ProfileId_Name");

            entity.HasOne(s => s.Profile)
                .WithMany(p => p.Secrets)
                .HasForeignKey(s => s.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // CARD-0106 S1. Two FILTERED unique indexes, not one composite: a project-scoped key and a
        // global key may deliberately share a name (that IS the override feature), while a duplicate
        // WITHIN a scope is a conflict. Filtered rather than leaning on PG15+ NULLS NOT DISTINCT, so
        // the constraint holds on every server version this deployment could land on.
        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.ToTable("ApiKeys");
            entity.HasKey(k => k.Id);
            entity.Property(k => k.Name).IsRequired().HasMaxLength(128);
            entity.Property(k => k.Ciphertext).IsRequired();
            entity.Property(k => k.ProtectionVersion).IsRequired().HasMaxLength(100);
            entity.Property(k => k.CreatedAt).IsRequired();
            entity.Property(k => k.UpdatedAt).IsRequired();

            entity.HasIndex(k => k.Name)
                .IsUnique()
                .HasFilter("\"ProjectId\" IS NULL")
                .HasDatabaseName("IX_ApiKeys_Name_Global");
            entity.HasIndex(k => new { k.ProjectId, k.Name })
                .IsUnique()
                .HasFilter("\"ProjectId\" IS NOT NULL")
                .HasDatabaseName("IX_ApiKeys_ProjectId_Name");

            entity.HasOne(k => k.Project)
                .WithMany(p => p.ApiKeys)
                .HasForeignKey(k => k.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentTuiModel>(entity =>
        {
            entity.ToTable("AgentTuiModels");
            entity.HasKey(m => m.Id);
            entity.Property(m => m.ProfileId).IsRequired();
            entity.Property(m => m.Identifier).IsRequired().HasMaxLength(500);
            entity.Property(m => m.DisplayName).IsRequired().HasMaxLength(200);
            entity.Property(m => m.Family).HasMaxLength(200);
            entity.Property(m => m.Source).IsRequired();
            entity.Property(m => m.Availability).IsRequired();
            entity.Property(m => m.RunnerVersion).HasMaxLength(200);
            entity.Property(m => m.IsSuggestedDefault).IsRequired();
            entity.Property(m => m.CreatedAt).IsRequired();
            entity.Property(m => m.UpdatedAt).IsRequired();

            entity.HasIndex(m => new { m.ProfileId, m.Identifier })
                .IsUnique()
                .HasDatabaseName("IX_AgentTuiModels_ProfileId_Identifier");

            entity.HasOne(m => m.Profile)
                .WithMany(p => p.Models)
                .HasForeignKey(m => m.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentTuiValidationRun>(entity =>
        {
            entity.ToTable("AgentTuiValidationRuns");
            entity.HasKey(v => v.Id);
            entity.Property(v => v.ProfileId).IsRequired();
            entity.Property(v => v.ProfileRevisionId).IsRequired();
            entity.Property(v => v.Operation).IsRequired().HasMaxLength(50);
            entity.Property(v => v.Status).IsRequired();
            entity.Property(v => v.ResultsJson).IsRequired().HasColumnType("jsonb");
            entity.Property(v => v.CapabilitiesJson).IsRequired().HasColumnType("jsonb");
            entity.Property(v => v.RunnerVersion).HasMaxLength(200);
            entity.Property(v => v.Summary).HasMaxLength(4000);
            entity.Property(v => v.CreatedAt).IsRequired();

            entity.HasIndex(v => new { v.ProfileId, v.CreatedAt })
                .HasDatabaseName("IX_AgentTuiValidationRuns_ProfileId_CreatedAt");

            entity.HasOne(v => v.Profile)
                .WithMany(p => p.ValidationRuns)
                .HasForeignKey(v => v.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(v => v.ProfileRevision)
                .WithMany()
                .HasForeignKey(v => new { v.ProfileId, v.ProfileRevisionId })
                .HasPrincipalKey(r => new { r.ProfileId, r.Id })
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Agent>(entity =>
        {
            entity.ToTable("Agents");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Name).IsRequired().HasMaxLength(200);
            entity.Property(a => a.Slug).IsRequired().HasMaxLength(120);
            entity.Property(a => a.WorkingDirectory).IsRequired().HasMaxLength(1000);
            entity.Property(a => a.Details).IsRequired().HasMaxLength(4000);
            entity.Property(a => a.AssignmentPolicy).IsRequired();
            entity.Property(a => a.Status).IsRequired();
            // NO HasDefaultValue here: a database default makes EF treat the property as
            // value-generated-on-add, so a CLR value equal to default(enum) — which is Frontier=0 —
            // is OMITTED from the INSERT and the database default silently wins. Frontier was the
            // one tier an agent row could not be created with (CARD-0016, live miss 2026-08-09:
            // every Frontier-dispatched delegate row read High). The entity's own initializer
            // keeps High as the default for creators that don't set it.
            entity.Property(a => a.ModelLevel).IsRequired();
            // Same reasoning as ModelLevel above, and it bites harder here: Normal IS 0, so a
            // HasDefaultValue would make EF omit every explicitly-chosen Normal from the INSERT.
            // The migration's column default backfills existing rows; the model never relies on it.
            entity.Property(a => a.ReplyStyle).IsRequired();
            // Unlike the two above, a default IS wanted here — ClaudeCode is 1, not 0, so the EF
            // sentinel (default(AgentKind) == Raw) can never collide with a legitimately chosen
            // value, and the column default is what states the FACT that every pre-CARD-0084 agent
            // row ran Claude. Same shape as AgentTasks.AgentKind, one slice earlier.
            entity.Property(a => a.Kind).IsRequired().HasDefaultValue(AgentKind.ClaudeCode);
            entity.Property(a => a.ModelId).HasMaxLength(500);
            entity.Property(a => a.PersistentSessionId).HasMaxLength(200);
            // CARD-0106 S2. jsonb, like every other JSON column here; the default states the fact
            // that every agent row predating the column carried no launch environment at all.
            entity.Property(a => a.LaunchEnvJson)
                .IsRequired()
                .HasColumnType("jsonb")
                .HasDefaultValue("{}");
            entity.Property(a => a.CreatedAt).IsRequired();
            entity.Property(a => a.UpdatedAt).IsRequired();

            entity.HasIndex(a => a.Slug).IsUnique().HasDatabaseName("IX_Agents_Slug");
            entity.HasIndex(a => a.Status).HasDatabaseName("IX_Agents_Status");
            entity.HasIndex(a => a.BoardId).HasDatabaseName("IX_Agents_BoardId");
            entity.HasIndex(a => a.TuiProfileId).HasDatabaseName("IX_Agents_TuiProfileId");

            entity.HasOne(a => a.DefaultWorkflowTemplate)
                .WithMany()
                .HasForeignKey(a => a.DefaultWorkflowTemplateId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(a => a.CurrentCard)
                .WithMany()
                .HasForeignKey(a => a.CurrentCardId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(a => a.Board)
                .WithMany()
                .HasForeignKey(a => a.BoardId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(a => a.TuiProfile)
                .WithMany(p => p.Agents)
                .HasForeignKey(a => a.TuiProfileId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AgentBundleAttachment>(entity =>
        {
            entity.ToTable("AgentBundleAttachments");
            // Composite natural key: "this agent carries this bundle" is a fact that can only be
            // true once, so the database says so rather than a service checking for duplicates.
            entity.HasKey(a => new { a.AgentId, a.BundleKey });
            // Matches the longest key the catalog's own naming can produce with room to spare; the
            // value is a filename, not prose.
            entity.Property(a => a.BundleKey).IsRequired().HasMaxLength(100);
            entity.Property(a => a.Position).IsRequired();
            entity.Property(a => a.CreatedAt).IsRequired();

            entity.HasOne(a => a.Agent)
                .WithMany(agent => agent.BundleAttachments)
                .HasForeignKey(a => a.AgentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Card>(entity =>
        {
            entity.ToTable("Cards");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.BoardId).IsRequired();
            entity.Property(c => c.BoardColumnId).IsRequired();
            entity.Property(c => c.AgentQueuePosition);
            entity.Property(c => c.Identifier).IsRequired().HasMaxLength(100);
            entity.Property(c => c.Title).IsRequired().HasMaxLength(300);
            // text, not varchar(n): the ceiling belongs in CardService.MaxDescriptionLength, where
            // raising it is a one-line change instead of a migration. As varchar(4000) with no
            // application check, an over-long description sailed past validation and came back as a
            // raw 500 from Postgres ("22001: value too long") — the whole point of the fix.
            entity.Property(c => c.Description).HasColumnType("text");
            entity.Property(c => c.Priority).IsRequired();
            entity.Property(c => c.LabelsJson).IsRequired().HasColumnType("jsonb");
            entity.Property(c => c.Status).IsRequired();
            entity.Property(c => c.ConcurrencyToken).IsConcurrencyToken();
            entity.Property(c => c.CreatedAt).IsRequired();
            entity.Property(c => c.UpdatedAt).IsRequired();
            // Same treatment as Description, and for the same live failure: varchar(1000) turned a
            // long close-out reason into a 500 twice while closing CARD-0042 and CARD-0046, and a
            // review verdict had to be hand-trimmed to exactly 1000 characters to fit.
            entity.Property(c => c.TerminalReason).HasColumnType("text");
            entity.Property(c => c.ArchivedReason).HasColumnType("text");
            entity.Property(c => c.ArchivedBy).HasMaxLength(200);
            entity.Property(c => c.AutoDispatchHeldAt);
            entity.Property(c => c.RevisionCount).IsRequired();

            entity.HasIndex(c => c.BoardId).HasDatabaseName("IX_Cards_BoardId");
            entity.HasIndex(c => c.BoardColumnId).HasDatabaseName("IX_Cards_BoardColumnId");
            entity.HasIndex(c => c.OwnerSessionId).HasDatabaseName("IX_Cards_OwnerSessionId");
            entity.HasIndex(c => c.CurrentWorktreeId).HasDatabaseName("IX_Cards_CurrentWorktreeId");
            entity.HasIndex(c => c.AssignedAgentId).HasDatabaseName("IX_Cards_AssignedAgentId");
            entity.HasIndex(c => new { c.AssignedAgentId, c.AgentQueuePosition })
                .HasDatabaseName("IX_Cards_AssignedAgentId_AgentQueuePosition");
            entity.HasIndex(c => c.ActiveWorkflowRunId).HasDatabaseName("IX_Cards_ActiveWorkflowRunId");
            entity.HasIndex(c => new { c.BoardId, c.Identifier })
                .IsUnique()
                .HasDatabaseName("IX_Cards_BoardId_Identifier");
            entity.HasIndex(c => new { c.BoardId, c.Status })
                .HasDatabaseName("IX_Cards_BoardId_Status");

            entity.HasOne(c => c.Board)
                .WithMany(b => b.Cards)
                .HasForeignKey(c => c.BoardId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(c => c.BoardColumn)
                .WithMany(c => c.Cards)
                .HasForeignKey(c => c.BoardColumnId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(c => c.OwnerSession)
                .WithMany()
                .HasForeignKey(c => c.OwnerSessionId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(c => c.CurrentWorktree)
                .WithMany()
                .HasForeignKey(c => c.CurrentWorktreeId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(c => c.AssignedAgent)
                .WithMany(a => a.QueueCards)
                .HasForeignKey(c => c.AssignedAgentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(c => c.ActiveWorkflowRun)
                .WithOne()
                .HasForeignKey<Card>(c => c.ActiveWorkflowRunId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CardRevision>(entity =>
        {
            entity.ToTable("CardRevisions");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.CardId).IsRequired();
            entity.Property(r => r.RevisionNumber).IsRequired();
            entity.Property(r => r.Kind).IsRequired();
            entity.Property(r => r.Title).HasMaxLength(300);
            // text + application ceiling, matching Cards.Description — a revision holds a copy of
            // exactly that value, so a tighter column here would 500 on the very edit it records.
            entity.Property(r => r.Description).HasColumnType("text");
            entity.Property(r => r.LabelsJson).HasColumnType("jsonb");
            entity.Property(r => r.Reason).HasColumnType("text");
            // Matches Cards.TerminalReason: text, not varchar — a reopen snapshots the same value
            // the card held, and a tighter column here would 500 on the close-out it records.
            entity.Property(r => r.TerminalReason).HasColumnType("text");
            entity.Property(r => r.EditedBy).HasMaxLength(200);
            entity.Property(r => r.CreatedAt).IsRequired();

            entity.HasIndex(r => new { r.CardId, r.RevisionNumber })
                .IsUnique()
                .HasDatabaseName("IX_CardRevisions_CardId_RevisionNumber");

            entity.HasOne(r => r.Card)
                .WithMany(c => c.Revisions)
                .HasForeignKey(r => r.CardId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CardWorkflowRun>(entity =>
        {
            entity.ToTable("CardWorkflowRuns");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.CardId).IsRequired();
            entity.Property(r => r.AgentId).IsRequired();
            entity.Property(r => r.WorkflowName).IsRequired().HasMaxLength(200);
            entity.Property(r => r.WorkflowDefinitionSnapshot).IsRequired();
            entity.Property(r => r.Status).IsRequired();
            entity.Property(r => r.FailureReason).HasMaxLength(4000);
            entity.Property(r => r.CreatedAt).IsRequired();
            entity.Property(r => r.UpdatedAt).IsRequired();

            entity.HasIndex(r => r.CardId).HasDatabaseName("IX_CardWorkflowRuns_CardId");
            entity.HasIndex(r => r.AgentId).HasDatabaseName("IX_CardWorkflowRuns_AgentId");
            entity.HasIndex(r => new { r.CardId, r.Status }).HasDatabaseName("IX_CardWorkflowRuns_CardId_Status");
            entity.HasIndex(r => new { r.CardId, r.Id })
                .IsUnique()
                .HasDatabaseName("IX_CardWorkflowRuns_CardId_Id");

            entity.HasOne(r => r.Card)
                .WithMany(c => c.WorkflowRuns)
                .HasForeignKey(r => r.CardId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.Agent)
                .WithMany(a => a.WorkflowRuns)
                .HasForeignKey(r => r.AgentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(r => r.WorkflowTemplate)
                .WithMany()
                .HasForeignKey(r => r.WorkflowTemplateId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(r => r.CurrentStage)
                .WithMany()
                .HasForeignKey(r => r.CurrentStageId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CardWorkflowStage>(entity =>
        {
            entity.ToTable("CardWorkflowStages");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.CardWorkflowRunId).IsRequired();
            entity.Property(s => s.StageOrder).IsRequired();
            entity.Property(s => s.Name).IsRequired().HasMaxLength(200);
            entity.Property(s => s.ExecutorType).IsRequired().HasMaxLength(100);
            entity.Property(s => s.ModelName).HasMaxLength(200);
            entity.Property(s => s.SystemPrompt).HasMaxLength(4000);
            entity.Property(s => s.GateRequired).IsRequired();
            entity.Property(s => s.Status).IsRequired();
            entity.Property(s => s.ResultSummary).HasMaxLength(4000);
            entity.Property(s => s.FailureReason).HasMaxLength(4000);
            entity.Property(s => s.CreatedAt).IsRequired();
            entity.Property(s => s.UpdatedAt).IsRequired();

            entity.HasIndex(s => new { s.CardWorkflowRunId, s.StageOrder })
                .IsUnique()
                .HasDatabaseName("IX_CardWorkflowStages_RunId_StageOrder");
            entity.HasIndex(s => new { s.CardWorkflowRunId, s.Id })
                .IsUnique()
                .HasDatabaseName("IX_CardWorkflowStages_RunId_Id");

            entity.HasOne(s => s.CardWorkflowRun)
                .WithMany(r => r.Stages)
                .HasForeignKey(s => s.CardWorkflowRunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentSession>(entity =>
        {
            entity.ToTable("AgentSessions");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.CardId).IsRequired(false);
            entity.Property(s => s.DefinitionName).IsRequired().HasMaxLength(100);
            entity.Property(s => s.AgentKind).IsRequired();
            entity.Property(s => s.Status).IsRequired();
            entity.Property(s => s.Cwd).IsRequired().HasMaxLength(1000);
            entity.Property(s => s.Cols).IsRequired();
            entity.Property(s => s.Rows).IsRequired();
            entity.Property(s => s.CreatedAt).IsRequired();
            entity.Property(s => s.StartedAt).IsRequired();
            entity.Property(s => s.LastSeenAt).IsRequired();
            entity.Property(s => s.FailureReason).HasMaxLength(2000);
            entity.Property(s => s.DelegationTokenHash).HasMaxLength(64);
            entity.Property(s => s.EffectiveModelId).HasMaxLength(500);
            // Stamps, not text — one "key vhash8" per composed bundle. Generous but bounded: the
            // whole catalog stamped at once is a few hundred characters, and a column that could
            // grow without limit would be a composed-text store by accident.
            entity.Property(s => s.ComposedBundleStamp).HasMaxLength(2000);

            entity.HasIndex(s => s.DelegationTokenHash)
                .HasDatabaseName("IX_AgentSessions_DelegationTokenHash");
            entity.HasIndex(s => s.CardId).HasDatabaseName("IX_AgentSessions_CardId");
            entity.HasIndex(s => s.WorktreeId).HasDatabaseName("IX_AgentSessions_WorktreeId");
            entity.HasIndex(s => s.TuiProfileRevisionId).HasDatabaseName("IX_AgentSessions_TuiProfileRevisionId");
            entity.HasIndex(s => new { s.CardId, s.Status })
                .HasDatabaseName("IX_AgentSessions_CardId_Status");

            entity.HasOne(s => s.Card)
                .WithMany(c => c.AgentSessions)
                .HasForeignKey(s => s.CardId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(s => s.Worktree)
                .WithMany(w => w.AgentSessions)
                .HasForeignKey(s => s.WorktreeId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(s => s.TuiProfileRevision)
                .WithMany(r => r.Sessions)
                .HasForeignKey(s => s.TuiProfileRevisionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TranscriptEntry>(entity =>
        {
            entity.ToTable("TranscriptEntries");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.AgentSessionId).IsRequired();
            entity.Property(t => t.Sequence).IsRequired();
            entity.Property(t => t.Kind).IsRequired().HasMaxLength(40);
            entity.Property(t => t.Uuid).HasMaxLength(64);
            entity.Property(t => t.ParentUuid).HasMaxLength(64);
            entity.Property(t => t.Role).HasMaxLength(40);
            entity.Property(t => t.ToolName).HasMaxLength(200);
            entity.Property(t => t.ToolUseId).HasMaxLength(120);
            entity.Property(t => t.StopReason).HasMaxLength(60);
            entity.Property(t => t.ApiCallId).HasMaxLength(120);
            entity.Property(t => t.ApiErrorClass).HasMaxLength(60);
            entity.Property(t => t.Model).HasMaxLength(200);
            entity.Property(t => t.CreatedAt).IsRequired();

            // (AgentSessionId, Sequence) is the natural idempotency key for ingestion.
            entity.HasIndex(t => new { t.AgentSessionId, t.Sequence })
                .IsUnique()
                .HasDatabaseName("IX_TranscriptEntries_AgentSessionId_Sequence");

            // CARD-0072: the recovery sweep adopts IsApiError = true TurnEnd rows. Without a
            // partial index that is a growing full-table scan every minute.
            entity.HasIndex(t => t.IsApiError)
                .HasDatabaseName("IX_TranscriptEntries_IsApiError")
                .HasFilter("\"IsApiError\" = true");

            entity.HasOne(t => t.AgentSession)
                .WithMany()
                .HasForeignKey(t => t.AgentSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApiErrorRecovery>(entity =>
        {
            entity.ToTable("ApiErrorRecoveries");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.AgentSessionId).IsRequired();
            entity.Property(r => r.StubSequence).IsRequired();
            entity.Property(r => r.StubUuid).HasMaxLength(64);
            entity.Property(r => r.Classification).IsRequired();
            entity.Property(r => r.ApiErrorClass).HasMaxLength(60);
            entity.Property(r => r.DetectedAt).IsRequired();
            entity.Property(r => r.AttemptCount).IsRequired();
            entity.Property(r => r.ResolvedReason).HasMaxLength(80);

            entity.HasIndex(r => new { r.AgentSessionId, r.StubSequence })
                .IsUnique()
                .HasDatabaseName("IX_ApiErrorRecoveries_AgentSessionId_StubSequence");
            entity.HasIndex(r => r.NextAttemptAt)
                .HasDatabaseName("IX_ApiErrorRecoveries_NextAttemptAt");

            entity.HasOne(r => r.AgentSession)
                .WithMany()
                .HasForeignKey(r => r.AgentSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SessionQueuedMessage>(entity =>
        {
            entity.ToTable("SessionQueuedMessages");
            entity.HasKey(m => m.Id);
            entity.Property(m => m.AgentSessionId).IsRequired();
            entity.Property(m => m.Body).IsRequired().HasColumnType("text");
            entity.Property(m => m.Status).IsRequired();
            entity.Property(m => m.Sequence).IsRequired();
            entity.Property(m => m.ContentDigest).HasColumnType("text");
            entity.Property(m => m.NoteHeader).HasColumnType("text");
            entity.Property(m => m.CreatedAt).IsRequired();
            entity.Property(m => m.DeliveryAttempts).IsRequired().HasDefaultValue(0);

            // Pending messages for a session are flushed in FIFO order.
            entity.HasIndex(m => new { m.AgentSessionId, m.Status, m.Sequence })
                .HasDatabaseName("IX_SessionQueuedMessages_AgentSessionId_Status_Sequence");

            // CARD-0067: the channel-reply correlation sweep is a GLOBAL query over the handful of
            // rows still owed a reply, so it gets a partial index rather than a table scan.
            entity.HasIndex(m => new { m.Origin, m.Status })
                .HasDatabaseName("IX_SessionQueuedMessages_OpenChannelCorrelations")
                .HasFilter("\"ChannelReplySettledAt\" IS NULL");

            entity.HasOne(m => m.AgentSession)
                .WithMany()
                .HasForeignKey(m => m.AgentSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RunAttempt>(entity =>
        {
            entity.ToTable("RunAttempts");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.CardId).IsRequired();
            entity.Property(a => a.AttemptNumber).IsRequired();
            entity.Property(a => a.Phase).IsRequired();
            entity.Property(a => a.CreatedAt).IsRequired();
            entity.Property(a => a.StartedAt).IsRequired();
            entity.Property(a => a.LastEventAt).IsRequired();
            entity.Property(a => a.PhaseStartedAt).IsRequired();
            entity.Property(a => a.PhaseDurationsJson).IsRequired().HasColumnType("jsonb");
            entity.Property(a => a.Prompt).IsRequired();
            entity.Property(a => a.ErrorDetails).HasMaxLength(4000);

            entity.HasIndex(a => a.CardId).HasDatabaseName("IX_RunAttempts_CardId");
            entity.HasIndex(a => a.AgentSessionId).HasDatabaseName("IX_RunAttempts_AgentSessionId");
            entity.HasIndex(a => a.WorktreeId).HasDatabaseName("IX_RunAttempts_WorktreeId");
            entity.HasIndex(a => a.BoardWorkflowDefinitionId).HasDatabaseName("IX_RunAttempts_BoardWorkflowDefinitionId");
            entity.HasIndex(a => new { a.CardId, a.AttemptNumber })
                .IsUnique()
                .HasDatabaseName("IX_RunAttempts_CardId_AttemptNumber");

            entity.HasOne(a => a.Card)
                .WithMany(c => c.RunAttempts)
                .HasForeignKey(a => a.CardId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.AgentSession)
                .WithMany(s => s.RunAttempts)
                .HasForeignKey(a => a.AgentSessionId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(a => a.Worktree)
                .WithMany(w => w.RunAttempts)
                .HasForeignKey(a => a.WorktreeId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(a => a.BoardWorkflowDefinition)
                .WithMany()
                .HasForeignKey(a => a.BoardWorkflowDefinitionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Worktree>(entity =>
        {
            entity.ToTable("Worktrees");
            entity.HasKey(w => w.Id);
            entity.Property(w => w.CardId).IsRequired();
            entity.Property(w => w.RepoPath).IsRequired().HasMaxLength(1000);
            entity.Property(w => w.Path).IsRequired().HasMaxLength(1000);
            entity.Property(w => w.Branch).IsRequired().HasMaxLength(500);
            entity.Property(w => w.BaseRef).IsRequired().HasMaxLength(500);
            entity.Property(w => w.Status).IsRequired();
            entity.Property(w => w.CreatedAt).IsRequired();
            entity.Property(w => w.LastTouchedAt).IsRequired();

            entity.HasIndex(w => w.CardId).HasDatabaseName("IX_Worktrees_CardId");
            entity.HasIndex(w => w.Path).IsUnique().HasDatabaseName("IX_Worktrees_Path");
            entity.HasIndex(w => w.Branch).HasDatabaseName("IX_Worktrees_Branch");
            entity.HasIndex(w => new { w.Status, w.LastTouchedAt })
                .HasDatabaseName("IX_Worktrees_Status_LastTouchedAt");

            entity.HasOne(w => w.Card)
                .WithMany(c => c.Worktrees)
                .HasForeignKey(w => w.CardId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BoardWorkflowDefinition>(entity =>
        {
            entity.ToTable("BoardWorkflowDefinitions");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.BoardId).IsRequired();
            entity.Property(d => d.Version).IsRequired();
            entity.Property(d => d.Name).IsRequired().HasMaxLength(200);
            entity.Property(d => d.Content).IsRequired();
            entity.Property(d => d.IsActive).IsRequired();
            entity.Property(d => d.CreatedAt).IsRequired();
            entity.Property(d => d.UpdatedAt).IsRequired();

            entity.HasIndex(d => new { d.BoardId, d.Version })
                .IsUnique()
                .HasDatabaseName("IX_BoardWorkflowDefinitions_BoardId_Version");
            entity.HasIndex(d => new { d.BoardId, d.IsActive })
                .HasDatabaseName("IX_BoardWorkflowDefinitions_BoardId_IsActive");

            entity.HasOne(d => d.Board)
                .WithMany(b => b.WorkflowDefinitions)
                .HasForeignKey(d => d.BoardId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExternalIssueRef>(entity =>
        {
            entity.ToTable("ExternalIssueRefs");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.CardId).IsRequired();
            entity.Property(r => r.TrackerKind).IsRequired();
            entity.Property(r => r.ExternalId).IsRequired().HasMaxLength(200);
            entity.Property(r => r.ExternalKey).IsRequired().HasMaxLength(200);
            entity.Property(r => r.Url).HasMaxLength(1000);
            entity.Property(r => r.RawPayloadJson).IsRequired().HasColumnType("jsonb");
            entity.Property(r => r.LastSyncedAt).IsRequired();

            entity.HasIndex(r => r.CardId).IsUnique().HasDatabaseName("IX_ExternalIssueRefs_CardId");
            entity.HasIndex(r => new { r.TrackerKind, r.ExternalId })
                .IsUnique()
                .HasDatabaseName("IX_ExternalIssueRefs_TrackerKind_ExternalId");

            entity.HasOne(r => r.Card)
                .WithOne(c => c.ExternalIssueRef)
                .HasForeignKey<ExternalIssueRef>(r => r.CardId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RetrySchedule>(entity =>
        {
            entity.ToTable("RetrySchedules");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.CardId).IsRequired();
            entity.Property(r => r.AttemptCount).IsRequired();
            entity.Property(r => r.MaxAttempts).IsRequired();
            entity.Property(r => r.LastError).HasMaxLength(4000);

            entity.HasIndex(r => r.CardId).IsUnique().HasDatabaseName("IX_RetrySchedules_CardId");
            entity.HasIndex(r => r.NextRetryAt).HasDatabaseName("IX_RetrySchedules_NextRetryAt");

            entity.HasOne(r => r.Card)
                .WithOne(c => c.RetrySchedule)
                .HasForeignKey<RetrySchedule>(r => r.CardId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TokenUsage>(entity =>
        {
            entity.ToTable("TokenUsages");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.RunAttemptId).IsRequired();
            entity.Property(t => t.TokensIn).IsRequired();
            entity.Property(t => t.TokensOut).IsRequired();
            entity.Property(t => t.CostUsd).IsRequired().HasPrecision(18, 6);
            entity.Property(t => t.ModelName).IsRequired().HasMaxLength(200);
            entity.Property(t => t.CreatedAt).IsRequired();

            entity.HasIndex(t => t.RunAttemptId).IsUnique().HasDatabaseName("IX_TokenUsages_RunAttemptId");

            entity.HasOne(t => t.RunAttempt)
                .WithOne(a => a.TokenUsage)
                .HasForeignKey<TokenUsage>(t => t.RunAttemptId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ArtifactSectionReview>(entity =>
        {
            entity.ToTable("ArtifactSectionReviews");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.StageExecutionId).IsRequired();
            entity.Property(r => r.SectionPath).IsRequired().HasMaxLength(1000);
            entity.Property(r => r.ContentHash).IsRequired().HasMaxLength(64);
            entity.Property(r => r.ReviewedAt).IsRequired();

            entity.HasIndex(r => r.StageExecutionId)
                .HasDatabaseName("IX_ArtifactSectionReviews_StageExecutionId");
            entity.HasIndex(r => new { r.StageExecutionId, r.SectionPath })
                .IsUnique()
                .HasDatabaseName("IX_ArtifactSectionReviews_StageExecutionId_SectionPath");

            entity.HasOne(r => r.StageExecution)
                .WithMany()
                .HasForeignKey(r => r.StageExecutionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentTask>(entity =>
        {
            entity.ToTable("AgentTasks");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.RootTaskId).IsRequired();
            entity.Property(t => t.Depth).IsRequired();
            entity.Property(t => t.Title).IsRequired().HasMaxLength(300);
            entity.Property(t => t.Goal).IsRequired().HasMaxLength(20000);
            entity.Property(t => t.Kind).IsRequired();
            entity.Property(t => t.Role).IsRequired();
            entity.Property(t => t.ProjectId).IsRequired(false);
            // CARD-0084 S2. ClaudeCode on every pre-existing row, which is what they actually ran —
            // the default is a backfill, not a guess.
            entity.Property(t => t.AgentKind).IsRequired().HasDefaultValue(AgentKind.ClaudeCode);
            entity.Property(t => t.ModelLevel).IsRequired();
            entity.Property(t => t.Workspace).IsRequired().HasDefaultValue(WorkspaceMode.Shared);
            entity.Property(t => t.WorkingDirectory).IsRequired().HasMaxLength(1000);
            entity.Property(t => t.RepoPath).HasMaxLength(1000);
            entity.Property(t => t.WorktreePath).HasMaxLength(1000);
            entity.Property(t => t.WorktreeBranch).HasMaxLength(300);
            entity.Property(t => t.MergeTargetRef).HasMaxLength(300);
            entity.Property(t => t.AgentName).HasMaxLength(200);
            entity.Property(t => t.ScopeGlob).HasMaxLength(1000);
            entity.Property(t => t.Status).IsRequired();
            entity.Property(t => t.ReplyTo).IsRequired();
            entity.Property(t => t.FailureReason).HasMaxLength(4000);
            entity.Property(t => t.LastPolledResultHash).HasColumnType("text");
            entity.Property(t => t.ResultFilePath).HasMaxLength(1000);
            entity.Property(t => t.TokenHash).HasMaxLength(128);
            entity.Property(t => t.CacheReadTokens).IsRequired();
            entity.Property(t => t.CacheCreationTokens).IsRequired();
            entity.Property(t => t.CostUsd).HasPrecision(18, 6);
            // 0 on every pre-existing row: priced before CARD-0023, and labelled as such.
            entity.Property(t => t.CostPricingVersion).IsRequired().HasDefaultValue(0);
            entity.Property(t => t.CreatedAt).IsRequired();
            entity.Property(t => t.ConcurrencyToken).IsRequired();
            // CARD-0047. Pre-existing rows get the shipped default expectation and no schedule:
            // a task dispatched before this migration is never retro-armed.
            entity.Property(t => t.ExpectedDurationMinutes).IsRequired().HasDefaultValue(10);
            entity.Property(t => t.CheckCount).IsRequired().HasDefaultValue(0);

            entity.HasIndex(t => new { t.RootTaskId, t.CreatedAt }).HasDatabaseName("IX_AgentTasks_RootTaskId_CreatedAt");
            entity.HasIndex(t => t.Status).HasDatabaseName("IX_AgentTasks_Status");
            entity.HasIndex(t => t.ParentTaskId).HasDatabaseName("IX_AgentTasks_ParentTaskId");
            entity.HasIndex(t => t.AgentSessionId).HasDatabaseName("IX_AgentTasks_AgentSessionId");
            entity.HasIndex(t => t.TokenHash).HasDatabaseName("IX_AgentTasks_TokenHash");

            // Self-reference: deleting a parent must NOT cascade a whole subtree away — the tree is
            // the audit trail. Restrict forces an explicit decision instead.
            entity.HasOne(t => t.ParentTask)
                .WithMany(t => t.Children)
                .HasForeignKey(t => t.ParentTaskId)
                .OnDelete(DeleteBehavior.Restrict);

            // A deleted project must leave delegation history intact; its old tasks simply fall
            // back to global-only scope if they are ever retried (CARD-0115 S1).
            entity.HasOne<Project>()
                .WithMany()
                .HasForeignKey(t => t.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);

        });

        modelBuilder.Entity<AgentTaskEvent>(entity =>
        {
            entity.ToTable("AgentTaskEvents");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AgentTaskId).IsRequired();
            entity.Property(e => e.Type).IsRequired();
            entity.Property(e => e.Detail).IsRequired().HasMaxLength(4000);
            entity.Property(e => e.At).IsRequired();

            entity.HasIndex(e => new { e.AgentTaskId, e.At }).HasDatabaseName("IX_AgentTaskEvents_AgentTaskId_At");

            entity.HasOne(e => e.AgentTask)
                .WithMany(t => t.Events)
                .HasForeignKey(e => e.AgentTaskId)
                .OnDelete(DeleteBehavior.Cascade);
        });

    }
}
