using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class AppDbContextModelTests
{
    [Test]
    public void Current_model_matches_migration_snapshot()
    {
        using var db = NewContext();
        db.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    [Test]
    public void Model_sql_fragments_have_no_carriage_returns()
    {
        using var db = NewContext();
        var model = db.GetService<IDesignTimeModel>().Model;
        var checkedFragments = 0;
        var offenders = new List<string>();

        void Check(string location, string? sql)
        {
            if (sql is null)
                return;

            checkedFragments++;
            if (sql.Contains('\r'))
                offenders.Add(location);
        }

        foreach (var entity in model.GetEntityTypes())
        {
            foreach (var index in entity.GetIndexes())
                Check($"{entity.Name} index {index.GetDatabaseName()} filter", index.GetFilter());

            foreach (var property in entity.GetProperties())
            {
                Check($"{entity.Name}.{property.Name} computed SQL", property.GetComputedColumnSql());
                Check($"{entity.Name}.{property.Name} default SQL", property.GetDefaultValueSql());
            }

            foreach (var constraint in entity.GetCheckConstraints())
                Check($"{entity.Name} constraint {constraint.Name}", constraint.Sql);
        }

        checkedFragments.ShouldBeGreaterThan(0);
        offenders.ShouldBeEmpty($"Model SQL contains carriage returns: {string.Join(", ", offenders)}");
    }

    private static AppDbContext NewContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model_guard;Username=unused;Password=unused")
            .Options);
}
