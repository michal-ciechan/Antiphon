using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0100 V-1. The CardRelationships table refuses a duplicate pair and a self-reference, and
/// deleting either card removes its rows. Each test owns an isolated clone of the migrated template.
/// The migration pin names the predecessor of AddCardRelationships, never the newest migration (CARD-1452).
/// </summary>
[Category("Integration")]
public sealed class CardRelationshipSchemaTests
{
    [Test]
    public async Task C100_Duplicate_pair_is_refused_by_the_unique_index()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        db.Database.HasPendingModelChanges().ShouldBeFalse();
        var migrations = db.Database.GetMigrations().ToArray();
        // CARD-1452: pin AddCardRelationships to its predecessor, never as the newest migration.
        var position = Array.FindIndex(migrations, static name =>
            name.EndsWith("_AddCardRelationships", StringComparison.Ordinal));
        position.ShouldBeGreaterThan(0);
        migrations[position - 1].ShouldEndWith("_AddCardCleanupScheduling");
        var entity = db.Model.FindEntityType(typeof(CardRelationship));
        entity.ShouldNotBeNull();
        var pairIndex = entity.GetIndexes()
            .Single(index => index.GetDatabaseName() == "IX_CardRelationships_CardId_RelatedCardId_Kind");
        pairIndex.IsUnique.ShouldBeTrue();
        pairIndex.Properties.Select(property => property.Name).ShouldBe(
            new[] { nameof(CardRelationship.CardId), nameof(CardRelationship.RelatedCardId), nameof(CardRelationship.Kind) });

        var seed = await SeedAsync(db, "A", "B");
        db.CardRelationships.Add(Row(seed["A"], seed["B"]));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        db.CardRelationships.Add(Row(seed["A"], seed["B"]));
        var failure = await CaptureAsync(db);
        var duplicateAccepted = failure is null;
        duplicateAccepted.ShouldBeFalse();
        failure!.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        failure.ConstraintName.ShouldBe("IX_CardRelationships_CardId_RelatedCardId_Kind");
    }

    [Test]
    public async Task C100_Self_reference_is_refused_by_the_check_constraint()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var seed = await SeedAsync(db, "A");

        db.CardRelationships.Add(Row(seed["A"], seed["A"]));
        var failure = await CaptureAsync(db);
        var selfAccepted = failure is null;
        selfAccepted.ShouldBeFalse();
        failure!.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        failure.ConstraintName.ShouldBe("CK_CardRelationships_NotSelf");
    }

    [Test]
    public async Task C100_Deleting_either_card_cascades_its_relationship_rows()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var seed = await SeedAsync(db, "A", "B", "C", "B2");
        db.CardRelationships.AddRange(Row(seed["A"], seed["B"]), Row(seed["C"], seed["B"]));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // The blocker (RelatedCardId) side.
        await db.Cards.Where(c => c.Id == seed["B"]).ExecuteDeleteAsync();
        (await db.CardRelationships.CountAsync()).ShouldBe(0);

        // The blocked (CardId) side.
        db.CardRelationships.Add(Row(seed["A"], seed["B2"]));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await db.Cards.Where(c => c.Id == seed["A"]).ExecuteDeleteAsync();
        (await db.CardRelationships.CountAsync()).ShouldBe(0);
    }

    private static async Task<PostgresException?> CaptureAsync(AppDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
            return null;
        }
        catch (DbUpdateException ex)
        {
            return ex.InnerException.ShouldBeOfType<PostgresException>();
        }
    }

    private static CardRelationship Row(Guid cardId, Guid relatedCardId) => new()
    {
        Id = Guid.NewGuid(),
        CardId = cardId,
        RelatedCardId = relatedCardId,
        Kind = CardRelationshipKind.BlockedBy,
        CreatedAt = DateTime.UtcNow,
    };

    private static async Task<Dictionary<string, Guid>> SeedAsync(AppDbContext db, params string[] names)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = "c100 " + Guid.NewGuid().ToString("N"),
            GitRepositoryUrl = "https://example.test/c100.git",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "c100", CreatedAt = now, UpdatedAt = now };
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = "backlog",
            Name = "Backlog",
            CardStatus = CardStatus.Backlog,
            ColumnOrder = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AddRange(project, board, column);
        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var card = new Card
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                BoardColumnId = column.Id,
                Identifier = "C100-" + name,
                Title = name,
                Status = CardStatus.Backlog,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Cards.Add(card);
            ids[name] = card.Id;
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return ids;
    }
}
