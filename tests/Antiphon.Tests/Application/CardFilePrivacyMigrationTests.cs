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
[NotInParallel]
public class CardFilePrivacyMigrationTests
{
    [Test]
    public async Task Upgrade_backfills_existing_rows_without_reinterpreting_old_snapshots()
    {
        // The shared clone is already current, and host-load Downs refuse to discard durable
        // telemetry. This subject migrates forward from the schema that preceded card-file privacy.
        string[] migrations;
        await using (var meta = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only").Options))
            migrations = meta.Database.GetMigrations().ToArray();
        var position = Array.FindIndex(migrations, m => m.EndsWith("_CardFilePrivacy", StringComparison.Ordinal));
        position.ShouldBeGreaterThan(0);
        await using var isolated = await MigrationTestDatabase.CreateAsync(migrations[position - 1], CancellationToken.None);
        var options = TestDbFixture.CreateDbContextOptions(isolated.ConnectionString);
        var projectId = Guid.NewGuid(); var boardId = Guid.NewGuid(); var cardId = Guid.NewGuid(); var columnId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Projects" ("Id", "Name", "GitRepositoryUrl", "GitHubIntegrationEnabled",
                    "NotificationsEnabled", "CreatedAt", "UpdatedAt")
                VALUES ({projectId}, {"C408 synthetic"}, {""}, false, false, {now}, {now})
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Boards" ("Id", "ProjectId", "Name", "Description", "TrackerKind",
                    "MaxConcurrentSessions", "CreatedAt", "UpdatedAt")
                VALUES ({boardId}, {projectId}, {"synthetic"}, {""}, 0, 0, {now}, {now})
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "BoardColumns" ("Id", "BoardId", "StateKey", "Name", "ColumnOrder", "CardStatus",
                    "IsActive", "IsTerminal", "CreatedAt", "UpdatedAt")
                VALUES ({columnId}, {boardId}, {"backlog"}, {"Backlog"}, 0, 0, false, false, {now}, {now})
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Cards" ("Id", "BoardId", "BoardColumnId", "Identifier", "Title", "Description",
                    "Importance", "LabelsJson", "Status", "ConcurrencyToken", "CreatedAt", "UpdatedAt", "RevisionCount")
                VALUES ({cardId}, {boardId}, {columnId}, {"CARD-0001"}, {"C408_PUBLIC"}, {""},
                    0, {"[]"}::jsonb, 0, {Guid.NewGuid()}, {now}, {now}, 0)
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "CardRevisions" ("Id", "CardId", "RevisionNumber", "Kind", "Title", "Reason", "CreatedAt")
                VALUES ({Guid.NewGuid()}, {cardId}, 1, {(int)CardRevisionKind.ContentEdit}, {"old public"}, {"synthetic"}, {now})
                """);
            await db.GetService<IMigrator>().MigrateAsync();
        }
        await using var fresh = new AppDbContext(options);
        (await fresh.Boards.SingleAsync()).SyncCardFiles.ShouldBeFalse();
        (await fresh.Projects.SingleAsync()).RepositoryVisibility.ShouldBe(RepositoryVisibility.Unknown);
        var card = await fresh.Cards.SingleAsync();
        card.Title.ShouldBe("C408_PUBLIC"); card.PrivateNotes.ShouldBeEmpty(); card.CardFileVisibility.ShouldBe(CardFileVisibility.Inherit);
        var revision = await fresh.CardRevisions.SingleAsync();
        revision.PrivateNotes.ShouldBeNull(); revision.CardFileVisibility.ShouldBeNull();
        card.PrivateNotes = "  C408_ROUNDTRIP\n雪  "; card.CardFileVisibility = CardFileVisibility.Private;
        await fresh.SaveChangesAsync();
        await using var reopened = new AppDbContext(options);
        var retained = await reopened.Cards.SingleAsync();
        retained.PrivateNotes.ShouldBe("  C408_ROUNDTRIP\n雪  "); retained.CardFileVisibility.ShouldBe(CardFileVisibility.Private);
    }
}
