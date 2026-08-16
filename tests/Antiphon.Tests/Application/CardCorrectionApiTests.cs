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
/// The four CARD-0019 routes over real HTTP, through the real application composition. The service
/// tests in <see cref="CardCorrectionIntegrationTests"/> cover the behaviour; what only a
/// full-stack test can catch is the wiring — route paths, verbs, request-body binding and the
/// <c>?includeArchived</c> query parameter.
/// </summary>
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
public class CardCorrectionApiTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AntiphonWebAppFactory _factory;
    private Guid _projectId;

    public CardCorrectionApiTests(AntiphonWebAppFactory factory) => _factory = factory;

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
    public async Task Correcting_a_card_over_http_returns_the_new_text_and_the_superseded_history()
    {
        var (_, card) = await SeedAsync("Api correction board", "Wrong as filed", "The old body.");
        using var client = _factory.CreateClient();

        var patch = await client.PatchAsJsonAsync(
            $"/api/cards/{card.Id}/content",
            new UpdateCardContentRequest(
                card.ConcurrencyToken,
                "The old body was disproven.",
                Title: "Right as corrected",
                Description: "The new body.",
                EditedBy: "operator"));

        patch.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = (await patch.Content.ReadFromJsonAsync<CardDto>(Json))!;
        updated.Title.ShouldBe("Right as corrected");
        updated.Description.ShouldBe("The new body.");
        updated.RevisionCount.ShouldBe(1);

        var history = await client.GetFromJsonAsync<List<CardRevisionDto>>(
            $"/api/cards/{card.Id}/revisions", Json);
        history!.Count.ShouldBe(1);
        history[0].Kind.ShouldBe(CardRevisionKind.ContentEdit);
        history[0].Title.ShouldBe("Wrong as filed");
        history[0].Description.ShouldBe("The old body.");
        history[0].Reason.ShouldBe("The old body was disproven.");
        history[0].EditedBy.ShouldBe("operator");
    }

    // The failure that motivated the ceiling fix answered 500. It must never do that again, and
    // the message has to name the field and the limit for a programmatic caller to pre-check.
    [Test]
    public async Task An_over_ceiling_description_over_http_is_a_structured_error_never_a_500()
    {
        var (_, card) = await SeedAsync("Api ceiling board", "Grows", "Short.");
        using var client = _factory.CreateClient();

        var response = await client.PatchAsJsonAsync(
            $"/api/cards/{card.Id}/content",
            new UpdateCardContentRequest(
                card.ConcurrencyToken,
                "Appending context.",
                Description: new string('x', CardService.MaxDescriptionLength + 1)));

        ((int)response.StatusCode).ShouldBeLessThan(500);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").GetProperty("Description")[0].GetString()
            .ShouldNotBeNull().ShouldContain("20,000");
    }

    [Test]
    public async Task Archive_and_unarchive_over_http_hide_and_restore_the_card_on_the_board()
    {
        var (board, card) = await SeedAsync("Api archive board", "Filed by mistake", string.Empty);
        using var client = _factory.CreateClient();

        var archive = await client.PostAsJsonAsync(
            $"/api/cards/{card.Id}/archive",
            new ArchiveCardRequest(card.ConcurrencyToken, "Duplicate.", "operator"));
        archive.StatusCode.ShouldBe(HttpStatusCode.OK);
        var archived = (await archive.Content.ReadFromJsonAsync<CardDto>(Json))!;
        archived.ArchivedAt.ShouldNotBeNull();

        var hidden = await client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{board.Id}", Json);
        hidden!.Columns.SelectMany(c => c.Cards).Select(c => c.Id).ShouldNotContain(card.Id);

        var shown = await client.GetFromJsonAsync<BoardDetailDto>(
            $"/api/boards/{board.Id}?includeArchived=true", Json);
        shown!.Columns.SelectMany(c => c.Cards).Select(c => c.Id).ShouldContain(card.Id);

        var unarchive = await client.PostAsJsonAsync(
            $"/api/cards/{card.Id}/unarchive",
            new UnarchiveCardRequest(archived.ConcurrencyToken, "Not a duplicate after all."));
        unarchive.StatusCode.ShouldBe(HttpStatusCode.OK);

        var restored = await client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{board.Id}", Json);
        restored!.Columns.SelectMany(c => c.Cards).Select(c => c.Id).ShouldContain(card.Id);

        var history = await client.GetFromJsonAsync<List<CardRevisionDto>>(
            $"/api/cards/{card.Id}/revisions", Json);
        history!.Select(r => r.Kind).ShouldBe([CardRevisionKind.Unarchive, CardRevisionKind.Archive]);
    }

    /// <summary>Seeds one project, one board and one card through the real services.</summary>
    private async Task<(BoardDetailDto Board, CardDto Card)> SeedAsync(
        string boardName, string cardTitle, string cardDescription)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var boards = scope.ServiceProvider.GetRequiredService<BoardService>();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();

        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Api Card Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = Path.Combine(Path.GetTempPath(), $"antiphon-api-cards-{Guid.NewGuid():N}"),
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        _projectId = project.Id;

        var board = await boards.CreateAsync(
            new CreateBoardRequest(project.Id, boardName), CancellationToken.None);
        var card = await cards.CreateAsync(
            board.Id, new CreateCardRequest(null, cardTitle, cardDescription), CancellationToken.None);
        return (board, card);
    }
}
