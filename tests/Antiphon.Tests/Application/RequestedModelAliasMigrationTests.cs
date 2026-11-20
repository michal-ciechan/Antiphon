using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>An explicit requested model alias is an additive nullable column. A downgrade cannot drop it.</summary>
[Category("Unit")]
public sealed class RequestedModelAliasMigrationTests
{
    [Test]
    public async Task UpIsAdditiveAndDownRefusesDataLoss()
    {
        await Task.CompletedTask;
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only").Options);
        db.Database.HasPendingModelChanges().ShouldBeFalse();
        var names = db.Database.GetMigrations().ToArray();
        var current = names.Single(n => n.EndsWith("_AddRequestedModelAlias", StringComparison.Ordinal));
        var previous = names.Single(n => n.EndsWith("_AddLandVerificationFailure", StringComparison.Ordinal));
        Array.IndexOf(names, current).ShouldBe(Array.IndexOf(names, previous) + 1);
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations[current], db.Database.ProviderName!);
        migration.UpOperations.ShouldAllBe(op => op.GetType() == typeof(AddColumnOperation)
            || op.GetType() == typeof(SqlOperation));
        var column = migration.UpOperations.OfType<AddColumnOperation>().Single();
        column.Name.ShouldBe("RequestedModelAlias");
        column.Table.ShouldBe("AgentTasks");
        column.IsNullable.ShouldBeTrue();
        column.MaxLength.ShouldBe(80);
        var sql = string.Join('\n', migration.UpOperations.OfType<SqlOperation>().Select(op => op.Sql));
        sql.ShouldContain("SET LOCAL lock_timeout = '5s'");
        sql.ShouldNotContain("\r");
        var error = Should.Throw<NotSupportedException>(() => _ = migration.DownOperations);
        error.Message.ShouldBe("An explicit requested model alias cannot be discarded by a downgrade.");
    }
}
