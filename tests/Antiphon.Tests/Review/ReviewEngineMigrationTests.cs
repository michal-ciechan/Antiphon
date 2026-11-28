using Antiphon.Server.Application.Review;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

/// <summary>
/// AddReviewEngine only creates the review engine tables. Its Down refuses, and the tip-rewind detach
/// (<see cref="MigrationTestDatabase.DetachReviewEngineAsync"/>) names exactly those tables in a safe drop order.
/// </summary>
[Category("Unit")]
public sealed class ReviewEngineMigrationTests
{
    [Test]
    public async Task Up_creates_only_the_detached_tables_and_Down_refuses()
    {
        await Task.CompletedTask;
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only").Options);
        db.Database.HasPendingModelChanges().ShouldBeFalse();
        var names = db.Database.GetMigrations().ToArray();
        var previous = names.Single(n => n.EndsWith("_AddRequestedModelAlias", StringComparison.Ordinal));
        Array.IndexOf(names, MigrationTestDatabase.ReviewEngineMigration).ShouldBe(Array.IndexOf(names, previous) + 1);

        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(
            assembly.Migrations[MigrationTestDatabase.ReviewEngineMigration], db.Database.ProviderName!);
        var tables = MigrationTestDatabase.ReviewEngineTables;
        migration.UpOperations.ShouldAllBe(op => op is CreateTableOperation || op is CreateIndexOperation);
        var created = migration.UpOperations.OfType<CreateTableOperation>().ToArray();
        created.Select(t => t.Name).Order(StringComparer.Ordinal).ShouldBe(tables.Order(StringComparer.Ordinal));
        migration.UpOperations.OfType<CreateIndexOperation>().ShouldAllBe(i => tables.Contains(i.Table));

        // The detach drops in list order, so every referenced table must come after the tables that reference it.
        foreach (var table in created)
            foreach (var key in table.ForeignKeys)
            {
                tables.ShouldContain(key.PrincipalTable, $"{table.Name} references only review engine tables");
                Array.IndexOf(tables, key.PrincipalTable).ShouldBeGreaterThan(Array.IndexOf(tables, table.Name),
                    $"{table.Name} drops before {key.PrincipalTable}");
            }

        var error = Should.Throw<NotSupportedException>(() => _ = migration.DownOperations);
        error.Message.ShouldBe(MigrationTestDatabase.ReviewEngineDowngradeRefusal);
    }
}

/// <summary>
/// A real database at the predecessor, walked forward through AddReviewEngine. A stored review row refuses both
/// the downgrade and the tip-rewind detach; an empty copy detaches cleanly and its Up can run again.
/// </summary>
[Category("Integration")]
public sealed class ReviewEngineDatabaseMigrationTests
{
    private const string Predecessor = "20261120091515_AddRequestedModelAlias";

    [Test]
    public async Task Legacy_execution_keeps_its_summary_after_the_warnings_migration(CancellationToken ct)
    {
        await using var database = await MigrationTestDatabase.CreateAsync(MigrationTestDatabase.CallerReportReceiptMigration, ct);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(database.ConnectionString));
        var subjectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        const string summary = "A renamed-file\nstatus antiphon/review not posted: from file name\nA next-file";

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ReviewSubjects"
                ("Id", "Host", "Repo", "Title", "AuthorLogin", "OpenedByLogin", "BaseRef", "HeadRef",
                 "BaseSha", "HeadSha", "OwnerTeams", "OwnerUsers", "CreatedAt")
            VALUES ({subjectId}, 'GitHub', 'fixture/legacy', 'Legacy review', 'author', 'author', 'main', 'change',
                    repeat('a', 40), repeat('b', 40), ARRAY[]::text[], ARRAY[]::text[], now());
            INSERT INTO "ReviewRuns"
                ("Id", "SubjectId", "Status", "Stage", "Round", "BotOnlyRounds", "CostUsd",
                 "Categories", "WorkflowName", "StartedAt", "UpdatedAt")
            VALUES ({runId}, {subjectId}, 'Running', 'Intake', 1, 0, 0, ARRAY[]::text[], 'pr-review', now(), now());
            INSERT INTO "ReviewNodeExecutions"
                ("Id", "RunId", "NodeId", "Iteration", "Sequence", "Kind", "Status", "InputTokens",
                 "OutputTokens", "CostUsd", "Summary")
            VALUES ({executionId}, {runId}, 'delta', 1, 1, 'Code', 'Succeeded', 0, 0, 0, {summary});
            """, ct);

        await db.Database.MigrateAsync(MigrationTestDatabase.ReviewNodeExecutionWarningsMigration, ct);

        var execution = await db.ReviewNodeExecutions.AsNoTracking().SingleAsync(n => n.Id == executionId, ct);
        var output = ReviewService.ToDto(execution);
        output.Summary.ShouldBe(summary);
        output.Warnings.ShouldBeNull();
    }

    [Test]
    public async Task A_stored_review_row_refuses_the_downgrade_and_the_detach(CancellationToken ct)
    {
        await using var database = await MigrationTestDatabase.CreateAsync(Predecessor, ct);
        // The unmodified migration SQL and schema: the historical fixture interceptor is not on these options.
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database.ConnectionString).Options;
        await using var db = new AppDbContext(options);
        var before = (await db.Database.GetAppliedMigrationsAsync(ct)).ToArray();
        before.Last().ShouldBe(Predecessor);
        (await ReviewEngineTablesAsync(db, ct)).ShouldBeEmpty();

        using (TestTiming.BeginPhase(TestPhaseKind.DbMigrate, "migrate"))
            await db.Database.MigrateAsync(MigrationTestDatabase.ReviewEngineMigration, ct);

        var expected = MigrationTestDatabase.ReviewEngineTables.Order(StringComparer.Ordinal).ToArray();
        (await ReviewEngineTablesAsync(db, ct)).ShouldBe(expected);
        var after = before.Append(MigrationTestDatabase.ReviewEngineMigration).ToArray();
        (await db.Database.GetAppliedMigrationsAsync(ct)).ShouldBe(after);

        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ReviewWorkflowRevisions" ("Id", "Name", "Hash", "GraphJson", "CreatedAt")
            VALUES ({id}, {"pr-review"}, {new string('a', 64)}, {"{}"}::jsonb, {DateTime.UtcNow})
            """, ct);

        var downgrade = await Should.ThrowAsync<NotSupportedException>(() => db.Database.MigrateAsync(Predecessor, ct));
        downgrade.Message.ShouldBe(MigrationTestDatabase.ReviewEngineDowngradeRefusal);
        var detach = await Should.ThrowAsync<NotSupportedException>(() => MigrationTestDatabase.DetachHostLoadAsync(db, ct));
        detach.Message.ShouldBe(MigrationTestDatabase.ReviewEngineDowngradeRefusal);

        (await db.Database.GetAppliedMigrationsAsync(ct)).ShouldBe(after);
        (await ReviewEngineTablesAsync(db, ct)).ShouldBe(expected);
        (await db.Database.SqlQuery<int>($"""SELECT count(*)::int AS "Value" FROM "ReviewWorkflowRevisions" WHERE "Id" = {id}""")
            .SingleAsync(ct)).ShouldBe(1, "the refused detach keeps the stored row");

        // With the row gone the copy is empty: the detach drops every review engine table and the history row,
        // and the migration's Up can run again on what remains.
        await db.Database.ExecuteSqlRawAsync("""DELETE FROM "ReviewWorkflowRevisions";""", ct);
        await MigrationTestDatabase.DetachReviewEngineAsync(db, ct);
        (await ReviewEngineTablesAsync(db, ct)).ShouldBeEmpty();
        (await db.Database.GetAppliedMigrationsAsync(ct)).ShouldBe(before);

        await db.Database.MigrateAsync(MigrationTestDatabase.ReviewEngineMigration, ct);
        (await ReviewEngineTablesAsync(db, ct)).ShouldBe(expected);
    }

    private static async Task<string[]> ReviewEngineTablesAsync(AppDbContext db, CancellationToken ct)
    {
        var names = MigrationTestDatabase.ReviewEngineTables;
        var present = await db.Database.SqlQuery<string>($"""
            SELECT table_name::text AS "Value" FROM information_schema.tables
            WHERE table_schema = current_schema() AND table_name::text = ANY({names})
            """).ToListAsync(ct);
        return present.Order(StringComparer.Ordinal).ToArray();
    }
}
