using Antiphon.Server.Application.Services;
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

/// <summary>
/// CARD-0407 V-5's migration-upgrade half. The lifecycle tests seed rows in the CURRENT schema,
/// which cannot show what happens to a row that predates the feature. This drives the real
/// migrator down past <c>AddAgentTaskInternalDecisionPolicy</c>, writes to the row while the three
/// columns genuinely do not exist, and then upgrades through both S1 migrations.
/// </summary>
[Category("Integration")]
public class AgentTaskInternalDecisionMigrationTests
{
    private const string FirstMigrationSuffix = "_AddAgentTaskInternalDecisionPolicy";

    [Test]
    public async Task Card0407_V05_pre_feature_row_upgrades_to_a_null_policy_with_legacy_fields_intact()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

        var now = DateTime.UtcNow;
        var taskId = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            Title = "legacy delegation",
            Goal = "legacy goal",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            ModelLevel = AgentModelLevel.High,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.GetTempPath(),
            Status = AgentTaskStatus.Succeeded,
            Result = "legacy result",
            StandingAuthority = "land the remaining slices",
            AutoContinueOnWait = true,
            CreatedAt = now,
            // Written at head so the downgrade has something to drop: a row that merely never had
            // a policy could not tell a real column drop from a no-op.
            InternalDecisionPolicyJson = """{"version":1,"grants":[]}""",
            InternalDecisionPolicyHash = new string('a', 64),
            InternalDecisionAuditBaselineJson = """{"head":"deadbeef"}""",
        });
        await db.SaveChangesAsync();

        var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var first = Array.FindIndex(migrations, m => m.EndsWith(FirstMigrationSuffix, StringComparison.Ordinal));
        first.ShouldBeGreaterThan(0);
        migrations[^1].ShouldEndWith("_StoreInternalDecisionPolicyAsText");

        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[first - 1]);

        // The row is now genuinely pre-S1: no column to carry a grant, so nothing survived.
        (await PolicyColumnsAsync(db)).ShouldBeEmpty();

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"AgentTasks\" SET \"Result\" = 'legacy write while downgraded' WHERE \"Id\" = {taskId}");

        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();

        var upgraded = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        upgraded.InternalDecisionPolicyJson.ShouldBeNull();
        upgraded.InternalDecisionPolicyHash.ShouldBeNull();
        upgraded.InternalDecisionAuditBaselineJson.ShouldBeNull();
        upgraded.Title.ShouldBe("legacy delegation");
        upgraded.Goal.ShouldBe("legacy goal");
        upgraded.Kind.ShouldBe(AgentTaskKind.Worker);
        upgraded.Role.ShouldBe(AgentTaskRole.Code);
        upgraded.ModelLevel.ShouldBe(AgentModelLevel.High);
        upgraded.Workspace.ShouldBe(WorkspaceMode.Shared);
        upgraded.Status.ShouldBe(AgentTaskStatus.Succeeded);
        upgraded.Result.ShouldBe("legacy write while downgraded");
        upgraded.StandingAuthority.ShouldBe("land the remaining slices");
        upgraded.AutoContinueOnWait.ShouldBeTrue();
        upgraded.AutoContinuedAt.ShouldBeNull();

        // StoreInternalDecisionPolicyAsText ran too: jsonb would reorder the keys and make the
        // stored hash disagree with the reloaded document.
        var types = await PolicyColumnTypesAsync(db);
        types["InternalDecisionPolicyJson"].ShouldBe("text");
        types["InternalDecisionAuditBaselineJson"].ShouldBe("text");
        types["InternalDecisionPolicyHash"].ShouldBe("character varying");

        // An upgraded row is a normal row: it can be granted afterwards, byte-stable.
        var canonical = InternalDecisionPolicy.Serialize(
            InternalDecisionPolicy.Normalize(
                InternalDecisionFixtures.Sample(),
                AgentTaskRole.Code,
                WorkspaceMode.Shared,
                InternalDecisionFixtures.ManualGrantor(),
                InternalDecisionFixtures.GrantedAt)!);
        upgraded.InternalDecisionPolicyJson = canonical;
        upgraded.InternalDecisionPolicyHash = InternalDecisionPolicy.Hash(canonical);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var regranted = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        regranted.InternalDecisionPolicyJson.ShouldBe(canonical);
        regranted.InternalDecisionPolicyHash.ShouldBe(InternalDecisionPolicy.Hash(canonical));

        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        db.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    private static Task<List<string>> PolicyColumnsAsync(AppDbContext db) =>
        db.Database.SqlQueryRaw<string>(
            """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = current_schema()
              AND table_name = 'AgentTasks'
              AND column_name IN (
                  'InternalDecisionPolicyJson',
                  'InternalDecisionPolicyHash',
                  'InternalDecisionAuditBaselineJson')
            ORDER BY column_name
            """)
            .ToListAsync();

    private static async Task<Dictionary<string, string>> PolicyColumnTypesAsync(AppDbContext db)
    {
        var rows = await db.Database.SqlQueryRaw<string>(
            """
            SELECT column_name || '=' || data_type
            FROM information_schema.columns
            WHERE table_schema = current_schema()
              AND table_name = 'AgentTasks'
              AND column_name IN (
                  'InternalDecisionPolicyJson',
                  'InternalDecisionPolicyHash',
                  'InternalDecisionAuditBaselineJson')
            """)
            .ToListAsync();
        return rows.Select(r => r.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
    }
}
