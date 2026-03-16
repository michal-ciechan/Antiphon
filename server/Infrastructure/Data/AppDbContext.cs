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
    }
}
