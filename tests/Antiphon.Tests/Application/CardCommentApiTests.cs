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

/// <summary>CARD-0166 S3: GET/POST /api/cards/{id}/discussion; /comments session-inject stays untouched.</summary>
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
[Category("Integration")]
public class CardCommentApiTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AntiphonWebAppFactory _factory;
    private Guid _projectId;

    public CardCommentApiTests(AntiphonWebAppFactory factory) => _factory = factory;

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
        await db.CardComments.Where(c => cardIds.Contains(c.CardId)).ExecuteDeleteAsync();
        await db.CardRevisions.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.Cards.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => p.Id == _projectId).ExecuteDeleteAsync();
        _projectId = Guid.Empty;
    }

    [Test]
    public async Task Post_and_Get_discussion_round_trip_as_Antiphon_origin()
    {
        var (_, card) = await SeedAsync();
        using var client = _factory.CreateClient();

        var create = await client.PostAsJsonAsync(
            $"/api/cards/{card.Id}/discussion",
            new CreateCardDiscussionRequest("Hello discussion", "operator"));

        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = (await create.Content.ReadFromJsonAsync<CardDiscussionCommentDto>(Json))!;
        created.Body.ShouldBe("Hello discussion");
        created.Author.ShouldBe("operator");
        created.Origin.ShouldBe(CardCommentOrigin.Antiphon);
        created.ExternalCommentId.ShouldBeNull();
        created.SyncedAt.ShouldBeNull();

        var list = await client.GetFromJsonAsync<List<CardDiscussionCommentDto>>(
            $"/api/cards/{card.Id}/discussion", Json);
        list!.Count.ShouldBe(1);
        list[0].Id.ShouldBe(created.Id);
        list[0].Body.ShouldBe("Hello discussion");
    }

    [Test]
    public async Task External_rows_are_returned_by_GET_but_POST_always_creates_Antiphon_origin()
    {
        var (_, card) = await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.CardComments.Add(new CardComment
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            Body = "Imported from GH",
            Author = "alice",
            Origin = CardCommentOrigin.External,
            ExternalCommentId = "42",
            ExternalUrl = "https://github.test/acme/app/issues/1#issuecomment-42",
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        });
        await db.SaveChangesAsync();

        using var client = _factory.CreateClient();
        var list = await client.GetFromJsonAsync<List<CardDiscussionCommentDto>>(
            $"/api/cards/{card.Id}/discussion", Json);
        list!.Count.ShouldBe(1);
        list[0].Origin.ShouldBe(CardCommentOrigin.External);
        list[0].Author.ShouldBe("alice");

        var create = await client.PostAsJsonAsync(
            $"/api/cards/{card.Id}/discussion",
            new CreateCardDiscussionRequest("Operator reply"));
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = (await create.Content.ReadFromJsonAsync<CardDiscussionCommentDto>(Json))!;
        created.Origin.ShouldBe(CardCommentOrigin.Antiphon);

        var after = await client.GetFromJsonAsync<List<CardDiscussionCommentDto>>(
            $"/api/cards/{card.Id}/discussion", Json);
        after!.Count.ShouldBe(2);
        after[0].Origin.ShouldBe(CardCommentOrigin.External);
        after[1].Origin.ShouldBe(CardCommentOrigin.Antiphon);
    }

    [Test]
    public async Task Session_inject_comments_route_is_still_registered_and_distinct()
    {
        // Pin: POST /comments must remain the CardReviewService inject path, not discussion storage.
        var (_, card) = await SeedAsync();
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/cards/{card.Id}/comments",
            new CardCommentRequest("inject please"));

        // No live session ⇒ service returns a structured failure / 4xx / 409 — never creates a CardComment.
        response.StatusCode.ShouldNotBe(HttpStatusCode.Created);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.CardComments.CountAsync(c => c.CardId == card.Id)).ShouldBe(0);
    }

    private async Task<(BoardDetailDto Board, CardDto Card)> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var boards = scope.ServiceProvider.GetRequiredService<BoardService>();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();

        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Discussion Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = Path.Combine(Path.GetTempPath(), $"antiphon-discussion-{Guid.NewGuid():N}"),
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        _projectId = project.Id;

        var board = await boards.CreateAsync(
            new CreateBoardRequest(project.Id, $"Discussion Board {Guid.NewGuid():N}"),
            CancellationToken.None);
        var card = await cards.CreateAsync(
            board.Id,
            new CreateCardRequest(null, "Discuss me", "Body"),
            CancellationToken.None);
        return (board, card);
    }
}
