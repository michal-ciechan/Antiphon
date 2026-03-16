using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Enable JSONB column support for flexible config storage
        // Individual entity configurations will use .HasColumnType("jsonb") on properties
        // Entity DbSets and configurations will be added in later stories
    }
}
