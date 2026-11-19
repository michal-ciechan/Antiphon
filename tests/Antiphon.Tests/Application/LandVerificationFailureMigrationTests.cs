using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-1995: recorded land verification failures are additive and cannot be dropped by a downgrade.</summary>
[Category("Unit")]
public sealed class LandVerificationFailureMigrationTests
{
    [Test]
    public async Task C1995_UpIsAdditiveAndDownRefusesDataLoss()
    {
        await Task.CompletedTask;
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only").Options);
        db.Database.HasPendingModelChanges().ShouldBeFalse();
        var names = db.Database.GetMigrations().ToArray();
        var current = names.Single(n => n.EndsWith("_AddLandVerificationFailure", StringComparison.Ordinal));
        var summary = names.Single(n => n.EndsWith("_AddSessionTranscriptSummaries", StringComparison.Ordinal));
        Array.IndexOf(names, current).ShouldBe(Array.IndexOf(names, summary) + 1);
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations[current], db.Database.ProviderName!);
        migration.UpOperations.ShouldAllBe(op => op.GetType() == typeof(AddColumnOperation)
            || op.GetType() == typeof(CreateIndexOperation) || op.GetType() == typeof(SqlOperation));
        migration.UpOperations.OfType<AddColumnOperation>().Select(op => op.Name).Order().ShouldBe(
            ["VerificationFailingProjects", "VerificationFailureOutput", "VerificationFailureStep"]);
        migration.UpOperations.OfType<AddColumnOperation>().ShouldAllBe(op =>
            op.Table == "AgentTaskLandings" && op.IsNullable);
        var index = migration.UpOperations.OfType<CreateIndexOperation>().Single();
        index.Name.ShouldBe("IX_AgentTaskLandings_TargetBuildFailure");
        index.Filter.ShouldBe("\"VerificationFailureStep\" = 'build'");
        index.Columns.ShouldBe(["TargetBeforeSha", "UpdatedAt"]);
        var sql = string.Join('\n', migration.UpOperations.OfType<SqlOperation>().Select(op => op.Sql));
        sql.ShouldContain("SET LOCAL lock_timeout = '5s'");
        sql.ShouldNotContain("\r");
        var error = Should.Throw<NotSupportedException>(() => _ = migration.DownOperations);
        error.Message.ShouldBe("Recorded land verification failures cannot be discarded by a downgrade.");
    }
}
