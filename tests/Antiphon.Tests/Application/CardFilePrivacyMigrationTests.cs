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
[NotInParallel]
public class CardFilePrivacyMigrationTests
{
    [Test]
    public async Task Upgrade_backfills_existing_rows_without_reinterpreting_old_snapshots()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(isolated.ConnectionString);
        var projectId = Guid.NewGuid(); var boardId = Guid.NewGuid(); var cardId = Guid.NewGuid(); var columnId = Guid.NewGuid();
        await using (var db = new AppDbContext(options))
        {
            var now = DateTime.UtcNow;
            db.Projects.Add(new Project { Id = projectId, Name = "C408 synthetic", GitRepositoryUrl = "", CreatedAt = now, UpdatedAt = now });
            db.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "synthetic", CreatedAt = now, UpdatedAt = now });
            db.BoardColumns.Add(new BoardColumn { Id = columnId, BoardId = boardId, StateKey = "backlog", Name = "Backlog", CreatedAt = now, UpdatedAt = now });
            db.Cards.Add(new Card { Id = cardId, BoardId = boardId, BoardColumnId = columnId, Identifier = "CARD-0001", Title = "C408_PUBLIC", CreatedAt = now, UpdatedAt = now });
            db.CardRevisions.Add(new CardRevision { Id = Guid.NewGuid(), CardId = cardId, RevisionNumber = 1, Kind = CardRevisionKind.ContentEdit, Title = "old public", Reason = "synthetic", CreatedAt = now });
            await db.SaveChangesAsync();
            var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            var position = Array.FindIndex(migrations, m => m.EndsWith("_CardFilePrivacy"));
            position.ShouldBeGreaterThan(0);
            await db.GetService<IMigrator>().MigrateAsync(migrations[position - 1]);
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
