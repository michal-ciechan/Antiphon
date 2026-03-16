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
    }
}
