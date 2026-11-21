using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
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

[Category("Integration")]
public sealed class RequestedModelAliasDatabaseMigrationTests
{
    private const string Predecessor = "20261119045705_AddLandVerificationFailure";

    [Test]
    public async Task Migrate_up_adds_the_nullable_alias_and_refused_down_preserves_it(CancellationToken ct)
    {
        await using var database = await MigrationTestDatabase.CreateAsync(Predecessor, ct);
        // Use the unmodified migration SQL and schema. The historical fixture interceptor can
        // create the column itself and would mask a missing or broken Up.
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database.ConnectionString).Options;
        await using var db = new AppDbContext(options);
        var before = (await db.Database.GetAppliedMigrationsAsync(ct)).ToArray();
        before.Last().ShouldBe(Predecessor);
        (await ReadAliasColumnAsync(db, ct)).ShouldBeNull();

        using (TestTiming.BeginPhase(TestPhaseKind.DbMigrate, "requested model alias upgrade"))
            await db.Database.MigrateAsync(RequestedModelAliasSchema.MigrationId, ct);

        (await ReadAliasColumnAsync(db, ct)).ShouldBe("character varying(80) nullable=YES");
        var after = before.Append(RequestedModelAliasSchema.MigrationId).ToArray();
        (await db.Database.GetAppliedMigrationsAsync(ct)).ShouldBe(after);

        var id = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            CreatedAt = DateTime.UtcNow,
            RequestedModelAlias = "gpt-6.2-astra",
        });
        await db.SaveChangesAsync(ct);

        var error = await Should.ThrowAsync<NotSupportedException>(() => db.Database.MigrateAsync(Predecessor, ct));
        error.Message.ShouldBe("An explicit requested model alias cannot be discarded by a downgrade.");

        (await db.Database.GetAppliedMigrationsAsync(ct)).ShouldBe(after);
        (await db.AgentTasks.Where(task => task.Id == id).Select(task => task.RequestedModelAlias).SingleAsync(ct))
            .ShouldBe("gpt-6.2-astra");
    }

    private static Task<string?> ReadAliasColumnAsync(AppDbContext db, CancellationToken ct) =>
        db.Database.SqlQueryRaw<string>("""
            SELECT data_type || '(' || character_maximum_length || ') nullable=' || is_nullable AS "Value"
            FROM information_schema.columns
            WHERE table_schema = current_schema() AND table_name = 'AgentTasks'
              AND column_name = 'RequestedModelAlias'
            """).SingleOrDefaultAsync(ct);
}
