using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0098 S2: <c>PATCH /api/cards/{id}/position</c> wiring — identifier forms and problem codes.
/// Behaviour lives in <see cref="CardReorderIntegrationTests"/>.
/// </summary>
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
[Category("Integration")]
public class CardReorderApiTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AntiphonWebAppFactory _factory;
    private Guid _projectId;

    public CardReorderApiTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    [After(Test)]
    public async Task CleanupAsync()
    {
        if (_projectId == Guid.Empty)
            return;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var boardIds = await db.Boards.Where(b => b.ProjectId == _projectId).Select(b => b.Id).ToListAsync();
        var cardIds = await db.Cards.Where(c => boardIds.Contains(c.BoardId)).Select(c => c.Id).ToListAsync();
        await db.CardRevisions.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => p.Id == _projectId).ExecuteDeleteAsync();
        _projectId = Guid.Empty;
    }

    [Test]
    public async Task Position_over_http_accepts_card_nnnn_and_hash_n_refs()
    {
        var (_, alpha, bravo) = await SeedPairAsync("Api position board");
        using var client = _factory.CreateClient();

        var byIdentifier = await client.PatchAsJsonAsync(
            $"/api/cards/{alpha.Identifier}/position",
            new PlaceCardRequest(alpha.ConcurrencyToken, Before: bravo.Identifier),
            Json);
        byIdentifier.StatusCode.ShouldBe(HttpStatusCode.OK);
        var moved = (await byIdentifier.Content.ReadFromJsonAsync<CardDto>(Json))!;
        moved.Position.ShouldBe(1);
        moved.Identifier.ShouldBe(alpha.Identifier);

        var hashRef = "#" + int.Parse(alpha.Identifier.AsSpan("CARD-".Length)).ToString();
        bravo = (await client.GetFromJsonAsync<CardDto>($"/api/cards/{bravo.Id}", Json))!;
        var byHash = await client.PatchAsJsonAsync(
            $"/api/cards/{bravo.Id}/position",
            new PlaceCardRequest(bravo.ConcurrencyToken, After: hashRef),
            Json);
        byHash.StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = (await byHash.Content.ReadFromJsonAsync<CardDto>(Json))!;
        after.Id.ShouldBe(bravo.Id);
        after.Position.ShouldNotBeNull();
    }

    [Test]
    public async Task Position_over_http_returns_card_order_stale_and_unreachable_codes()
    {
        var (_, alpha, bravo) = await SeedPairAsync("Api position codes board");
        using var client = _factory.CreateClient();

        CardDto charlie;
        using (var scope = _factory.Services.CreateScope())
        {
            var cards = scope.ServiceProvider.GetRequiredService<CardService>();
            charlie = await cards.CreateAsync(
                alpha.BoardId, new CreateCardRequest(null, "Charlie"), CancellationToken.None);
        }

        await client.PatchAsJsonAsync(
            $"/api/cards/{charlie.Id}/position",
            new PlaceCardRequest(charlie.ConcurrencyToken, Placement: CardPlacement.Top),
            Json);

        alpha = (await client.GetFromJsonAsync<CardDto>($"/api/cards/{alpha.Id}", Json))!;
        var stale = await client.PatchAsJsonAsync(
            $"/api/cards/{alpha.Id}/position",
            new PlaceCardRequest(alpha.ConcurrencyToken, Before: charlie.Identifier, After: bravo.Identifier),
            Json);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var staleProblem = await stale.Content.ReadFromJsonAsync<JsonElement>();
        staleProblem.GetProperty("code").GetString().ShouldBe(CardService.CardOrderStaleCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Cards.Where(c => c.Id == alpha.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.DueAt, DateTime.UtcNow.AddDays(2)));
        }

        alpha = (await client.GetFromJsonAsync<CardDto>($"/api/cards/{alpha.Id}", Json))!;
        var unreachable = await client.PatchAsJsonAsync(
            $"/api/cards/{alpha.Id}/position",
            new PlaceCardRequest(alpha.ConcurrencyToken, Before: bravo.Identifier),
            Json);
        ((int)unreachable.StatusCode).ShouldBe(422);
        var problem = await unreachable.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().ShouldBe(CardService.CardPositionUnreachableCode);
    }

    [Test]
    public async Task Position_over_http_rejects_an_empty_body_choice()
    {
        var (_, alpha, _) = await SeedPairAsync("Api position empty board");
        using var client = _factory.CreateClient();

        var response = await client.PatchAsJsonAsync(
            $"/api/cards/{alpha.Id}/position",
            new PlaceCardRequest(alpha.ConcurrencyToken),
            Json);
        ((int)response.StatusCode).ShouldBe(422);
    }

    private async Task<(BoardDetailDto Board, CardDto Alpha, CardDto Bravo)> SeedPairAsync(string boardName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var boards = scope.ServiceProvider.GetRequiredService<BoardService>();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();

        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Api Reorder Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = Path.Combine(Path.GetTempPath(), $"antiphon-api-reorder-{Guid.NewGuid():N}"),
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        _projectId = project.Id;

        var board = await boards.CreateAsync(
            new CreateBoardRequest(project.Id, boardName), CancellationToken.None);
        var alpha = await cards.CreateAsync(
            board.Id, new CreateCardRequest(null, "Alpha"), CancellationToken.None);
        var bravo = await cards.CreateAsync(
            board.Id, new CreateCardRequest(null, "Bravo"), CancellationToken.None);
        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await db.Cards.Where(c => c.Id == alpha.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedAt, origin));
        await db.Cards.Where(c => c.Id == bravo.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedAt, origin.AddMinutes(1)));
        alpha = await cards.GetByIdAsync(alpha.Id, CancellationToken.None);
        bravo = await cards.GetByIdAsync(bravo.Id, CancellationToken.None);
        return (board, alpha, bravo);
    }
}
