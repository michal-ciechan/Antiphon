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

[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
[Category("Integration")]
public class BoardCardOrderApiTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AntiphonWebAppFactory _factory;
    private Guid _projectId;

    public BoardCardOrderApiTests(AntiphonWebAppFactory factory) => _factory = factory;

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
    public async Task Card_order_over_http_returns_listed_cards_and_requires_a_reason()
    {
        var (board, alpha, bravo) = await SeedPairAsync("Api card-order board");
        using var client = _factory.CreateClient();

        var ok = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/card-order",
            new ReorderBoardCardsRequest(
                [new ReorderBoardCardEntry(bravo.Identifier), new ReorderBoardCardEntry(alpha.Identifier)],
                "Bravo first."),
            Json);
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await ok.Content.ReadFromJsonAsync<ReorderBoardCardsResult>(Json))!;
        result.Cards.Select(c => c.Id).ShouldBe([bravo.Id, alpha.Id]);
        result.Cards[0].Position.ShouldBe(1);

        var missingReason = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/card-order",
            new { cards = new[] { new { id = alpha.Identifier } } },
            Json);
        ((int)missingReason.StatusCode).ShouldBe(422);
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
            Name = $"Api Bulk Order Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = Path.Combine(Path.GetTempPath(), $"antiphon-api-bulk-{Guid.NewGuid():N}"),
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
        return (board, alpha, bravo);
    }
}
