using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class PoolClaudeEffortMigrationTests
{
    [Test]
    public async Task C1491_nullable_stamp_roundtrips_and_legacy_rows_stay_unknown()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var templateId = Guid.NewGuid();
        var legacyId = Guid.NewGuid();
        db.Agents.Add(new Agent
        {
            Id = templateId, Name = "c1491 template", Slug = $"c1491-{templateId:N}",
            WorkingDirectory = "/c1491-legacy", Details = "legacy details", Kind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.High, IsPoolDelegate = true, PoolClaudeEffort = "xhigh",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var columns = await ColumnsAsync(db);
        var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var index = Array.FindIndex(migrations, m => m.EndsWith("_AddPoolClaudeEffort", StringComparison.Ordinal));
        index.ShouldBeGreaterThan(0);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[index - 1]);
        (await ColumnsAsync(db)).ShouldBe(columns.Where(c => c != "PoolClaudeEffort").ToArray(),
            "Down drops exactly the effort column");
        // Insert while the column genuinely does not exist. Copy the old schema's record shape,
        // replacing identity fields only; this SQL never names the new property.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Agents"
            SELECT (jsonb_populate_record(NULL::"Agents", to_jsonb(a) ||
                jsonb_build_object('Id', {legacyId}, 'Name', 'c1491 legacy', 'Slug', {"c1491-" + legacyId.ToString("N")}))).*
            FROM "Agents" a WHERE "Id" = {templateId}
            """);
        await migrator.MigrateAsync();
        (await ColumnsAsync(db)).ShouldBe(columns);
        db.ChangeTracker.Clear();
        var legacy = await db.Agents.SingleAsync(a => a.Id == legacyId);
        legacy.PoolClaudeEffort.ShouldBeNull();
        legacy.Name.ShouldBe("c1491 legacy");
        legacy.Details.ShouldBe("legacy details");
        legacy.WorkingDirectory.ShouldBe("/c1491-legacy");
        legacy.ModelLevel.ShouldBe(AgentModelLevel.High);
        legacy.IsPoolDelegate.ShouldBeTrue();
        var definition = await db.Database.SqlQueryRaw<string>("""
            SELECT data_type || ':' || is_nullable || ':' || COALESCE(column_default, '<none>') AS "Value"
            FROM information_schema.columns WHERE table_schema = current_schema()
              AND table_name = 'Agents' AND column_name = 'PoolClaudeEffort'
            """).SingleAsync();
        definition.ShouldBe("text:YES:<none>");
        legacy.PoolClaudeEffort = "xhigh";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        (await db.Agents.AsNoTracking().SingleAsync(a => a.Id == legacyId)).PoolClaudeEffort.ShouldBe("xhigh");
        db.Database.HasPendingModelChanges().ShouldBeFalse();
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        await migrator.MigrateAsync(migrations[index - 1]);
        (await ColumnsAsync(db)).ShouldBe(columns.Where(c => c != "PoolClaudeEffort").ToArray());
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        (await db.Agents.SingleAsync(a => a.Id == legacyId)).PoolClaudeEffort.ShouldBeNull();
    }

    private static Task<string[]> ColumnsAsync(AppDbContext db) => db.Database.SqlQueryRaw<string>("""
        SELECT column_name FROM information_schema.columns
        WHERE table_schema = current_schema() AND table_name = 'Agents' ORDER BY column_name
        """).ToArrayAsync();
}
