using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

[Category("Integration")]
public sealed class ReviewRunIdentityMigrationTests
{
    private const string Predecessor = "20261125151517_AddCallerReportSettlementReceipt";

    [Test]
    public async Task Up_closes_eight_duplicates_and_Down_preserves_the_history(CancellationToken ct)
    {
        await using var schema = await MigrationTestDatabase.CreateAsync(Predecessor, ct);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(schema.ConnectionString).Options);
        var start = DateTime.UtcNow.AddDays(-1);
        var statuses = new[] { "Failed", "Failed", "Failed", "Failed", "Failed", "Failed", "AwaitingFixes", "AwaitingFixes", "AtGate" };
        var keeper = Guid.Empty;
        for (var i = 0; i < statuses.Length; i++)
        {
            var repo = i % 2 == 0 ? "ACME/Widgets" : "acme/widgets";
            var subject = Guid.NewGuid();
            var run = Guid.NewGuid();
            var startedAt = start.AddMinutes(i);
            var stage = i < 4 ? "Review" : i < 6 ? "Publish" : i < 8 ? "AwaitingFixes" : "HumanGate";
            await db.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "ReviewSubjects"
                    ("Id", "Host", "Repo", "PrNumber", "Title", "AuthorLogin", "OpenedByLogin", "BaseRef", "HeadRef",
                     "BaseSha", "HeadSha", "OwnerTeams", "OwnerUsers", "CreatedAt")
                VALUES ({{subject}}, 'GitHub', {{repo}}, 56, 'Fix the lease', 'author', 'author', 'main', 'fix', '', '', '{}', '{}', {{startedAt}});
                INSERT INTO "ReviewRuns"
                    ("Id", "SubjectId", "Status", "Stage", "Round", "BotOnlyRounds", "CostUsd", "Categories",
                     "WorkflowName", "StartedAt", "UpdatedAt")
                VALUES ({{run}}, {{subject}}, {{statuses[i]}}, {{stage}}, 1, 0, 0, '{}', 'pr-review', {{startedAt}}, {{startedAt}});
                """, ct);
            keeper = run;
        }

        await db.Database.MigrateAsync(ct);

        (await db.Database.SqlQueryRaw<Guid>("""SELECT "Id" AS "Value" FROM "ReviewRuns" WHERE "Status" <> 'Closed'""")
            .ToArrayAsync(ct)).ShouldBe([keeper]);
        await AssertHistoryAsync(db, ct);

        await db.Database.MigrateAsync(Predecessor, ct);
        await AssertHistoryAsync(db, ct);
    }

    private static async Task AssertHistoryAsync(AppDbContext db, CancellationToken ct)
    {
        (await db.ReviewRuns.CountAsync(ct)).ShouldBe(9);
        (await db.ReviewSubjects.CountAsync(ct)).ShouldBe(9);
        (await db.ReviewNodeExecutions.CountAsync(n => n.Summary == "superseded by duplicate-intake cleanup", ct)).ShouldBe(8);
    }
}
