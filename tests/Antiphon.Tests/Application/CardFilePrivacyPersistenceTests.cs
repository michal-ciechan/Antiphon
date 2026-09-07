using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Data.Seeding;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
[ParallelLimiter<ProcessSpawnLimit>]
public class CardFilePrivacyPersistenceTests
{
    [Test]
    public async Task Disposable_database_copy_retains_notes_snapshots_and_policy_in_fresh_context()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = new CardFilePrivacyWorld(isolated.ConnectionString); await world.InitializeAsync();
        var cardId = await world.AddCardAsync(notes: " C408_COPY_CURRENT\n\u96ea ", visibility: CardFileVisibility.Private);
        await using (var db = world.Db())
        {
            db.CardRevisions.Add(new CardRevision { Id = Guid.NewGuid(), CardId = cardId, RevisionNumber = 1, Kind = CardRevisionKind.ContentEdit, PrivateNotes = "C408_COPY_OLD", CardFileVisibility = CardFileVisibility.Public, Reason = "synthetic", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var source = new NpgsqlConnectionStringBuilder(isolated.ConnectionString);
        System.Text.RegularExpressions.Regex.IsMatch(source.Database!, "^test_[a-f0-9]{32}$").ShouldBeTrue();
        using (var pool = new NpgsqlConnection(isolated.ConnectionString)) NpgsqlConnection.ClearPool(pool);
        var copiedName = "test_"+Guid.NewGuid().ToString("N");
        await using (var maintenance = new NpgsqlConnection(TestDbFixture.MaintenanceConnectionString))
        {
            await maintenance.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {copiedName} TEMPLATE {source.Database}", maintenance);
            await command.ExecuteNonQueryAsync();
        }
        source.Database = copiedName;
        await using var copy = new IsolatedTestSchema(copiedName, source.ConnectionString);
        await using var restored = new AppDbContext(TestDbFixture.CreateDbContextOptions(copy.ConnectionString));
        var cards = new CardService(restored, null!, null!, null!, new MockEventBus(), TimeProvider.System, null!);
        (await cards.GetPrivateNotesAsync(cardId, null, default)).PrivateNotes.ShouldBe(" C408_COPY_CURRENT\n\u96ea ");
        (await cards.GetPrivateNotesAsync(cardId, 1, default)).PrivateNotes.ShouldBe("C408_COPY_OLD");
        (await restored.Cards.SingleAsync(c => c.Id == cardId)).CardFileVisibility.ShouldBe(CardFileVisibility.Private);
        (await restored.Boards.SingleAsync(b => b.Id == world.BoardId)).SyncCardFiles.ShouldBeTrue();
        (await restored.Projects.SingleAsync(p => p.Id == world.ProjectId)).RepositoryVisibility.ShouldBe(RepositoryVisibility.Public);
    }

    [Test]
    public async Task Fresh_migrated_seeded_database_does_not_import_synthetic_card_markdown()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var repo = new ScratchGitRepo("c408-fresh-db"); await repo.CommitFileAsync("seed", "seed");
        var dir = Directory.CreateDirectory(Path.Combine(repo.Path, "docs/cards/legacy"));
        var file = Path.Combine(dir.FullName, "CARD-0408-legacy.md");
        await File.WriteAllTextAsync(file, "C408_MARKDOWN_IS_NOT_BACKUP\nprivateNotes: do not import");
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(isolated.ConnectionString));
        var project = new Project { Id = Guid.NewGuid(), Name = "C408 fresh", LocalRepositoryPath = repo.Path, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Projects.Add(project); await db.SaveChangesAsync();
        await DatabaseSeeder.SeedAsync(db, new LlmSettings(), default);
        (await db.Cards.AnyAsync()).ShouldBeFalse();
        (await db.Boards.AnyAsync(b => b.SyncCardFiles)).ShouldBeFalse();
        (await db.Projects.SingleAsync(p => p.Id == project.Id)).RepositoryVisibility.ShouldBe(RepositoryVisibility.Unknown);
        (await File.ReadAllTextAsync(file)).ShouldContain("C408_MARKDOWN_IS_NOT_BACKUP");
        File.Exists(Path.Combine(repo.Path, ".gitignore")).ShouldBeFalse();
    }
}
