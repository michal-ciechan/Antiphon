using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
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
/// CARD-0350 S2: create, update, clear, validate, project, and revision-log a card alias
/// through the real composition.
/// </summary>
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
[Category("Integration")]
public class CardAliasApiTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AntiphonWebAppFactory _factory;
    private Guid _projectId;

    public CardAliasApiTests(AntiphonWebAppFactory factory) => _factory = factory;

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
    public async Task Create_stores_a_normalized_alias_and_the_board_projects_it()
    {
        var (board, _) = await SeedAsync("Alias create board", "Long canonical title", "body");
        using var client = _factory.CreateClient();

        var create = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards",
            new CreateCardRequest(null, "Bounded check headers", Alias: "  Check   header  "),
            Json);
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = (await create.Content.ReadFromJsonAsync<CardDto>(Json))!;
        created.Alias.ShouldBe("Check header");
        created.Title.ShouldBe("Bounded check headers");

        var detail = await client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{board.Id}", Json);
        var projected = detail!.Columns.SelectMany(c => c.Cards).Single(c => c.Id == created.Id);
        projected.Alias.ShouldBe("Check header");

        using var scope = _factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Cards.SingleAsync(c => c.Id == created.Id);
        stored.Alias.ShouldBe("Check header");
    }

    [Test]
    public async Task Create_without_an_alias_stores_null()
    {
        var (_, card) = await SeedAsync("Alias absent board", "No short label", "body");
        card.Alias.ShouldBeNull();
    }

    [Test]
    public async Task Create_rejects_a_sixth_word_at_the_API_boundary()
    {
        var (board, _) = await SeedAsync("Alias invalid board", "Keep me", "body");
        using var client = _factory.CreateClient();

        var create = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards",
            new CreateCardRequest(null, "Too many words", Alias: "one two three four five six"),
            Json);

        ((int)create.StatusCode).ShouldBe(422);
        var problem = await create.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").GetProperty("Alias")[0].GetString()
            .ShouldBe("Alias must be at most 5 words; got 6.");
    }

    [Test]
    public async Task Service_create_rejects_a_newline_as_ValidationException()
    {
        var (board, _) = await SeedAsync("Alias service board", "Keep me", "body");
        using var scope = _factory.Services.CreateScope();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();

        var ex = await Should.ThrowAsync<ValidationException>(() =>
            cards.CreateAsync(
                board.Id,
                new CreateCardRequest(null, "Newlined", Alias: "Status\nStuck"),
                CancellationToken.None));
        ex.Errors[nameof(CreateCardRequest.Alias)].Single().ShouldBe("Alias must be a single line.");
    }

    [Test]
    public async Task Update_sets_clears_and_snapshots_the_superseded_alias()
    {
        var (_, card) = await SeedAsync("Alias edit board", "Canonical title", "body");
        using var client = _factory.CreateClient();

        var set = await client.PatchAsJsonAsync(
            $"/api/cards/{card.Id}/content",
            new UpdateCardContentRequest(card.ConcurrencyToken, "Give it a short label.", Alias: "Check header"),
            Json);
        set.StatusCode.ShouldBe(HttpStatusCode.OK);
        var withAlias = (await set.Content.ReadFromJsonAsync<CardDto>(Json))!;
        withAlias.Alias.ShouldBe("Check header");
        withAlias.Title.ShouldBe("Canonical title");

        var historyAfterSet = await client.GetFromJsonAsync<List<CardRevisionDto>>(
            $"/api/cards/{card.Id}/revisions", Json);
        historyAfterSet!.Count.ShouldBe(1);
        historyAfterSet[0].Kind.ShouldBe(CardRevisionKind.ContentEdit);
        historyAfterSet[0].Alias.ShouldBeNull();
        historyAfterSet[0].Title.ShouldBe("Canonical title");

        var clear = await client.PatchAsJsonAsync(
            $"/api/cards/{card.Id}/content",
            new UpdateCardContentRequest(withAlias.ConcurrencyToken, "No longer needed.", Alias: ""),
            Json);
        clear.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cleared = (await clear.Content.ReadFromJsonAsync<CardDto>(Json))!;
        cleared.Alias.ShouldBeNull();

        var history = await client.GetFromJsonAsync<List<CardRevisionDto>>(
            $"/api/cards/{card.Id}/revisions", Json);
        history!.Count.ShouldBe(2);
        history[0].Alias.ShouldBe("Check header");
        history[1].Alias.ShouldBeNull();
    }

    [Test]
    public async Task An_edit_that_omits_alias_leaves_the_stored_value_alone()
    {
        var (_, card) = await SeedAsync("Alias partial board", "Canonical title", "body");
        using var scope = _factory.Services.CreateScope();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();
        var withAlias = await cards.UpdateContentAsync(
            card.Id,
            new UpdateCardContentRequest(card.ConcurrencyToken, "Set alias.", Alias: "Keep me"),
            CancellationToken.None);

        var titled = await cards.UpdateContentAsync(
            withAlias.Id,
            new UpdateCardContentRequest(withAlias.ConcurrencyToken, "Title only.", Title: "New title"),
            CancellationToken.None);

        titled.Title.ShouldBe("New title");
        titled.Alias.ShouldBe("Keep me");
    }

    [Test]
    public async Task Update_rejects_an_over_length_alias_without_writing_a_revision()
    {
        var (_, card) = await SeedAsync("Alias ceiling board", "Canonical title", "body");
        using var client = _factory.CreateClient();

        var response = await client.PatchAsJsonAsync(
            $"/api/cards/{card.Id}/content",
            new UpdateCardContentRequest(
                card.ConcurrencyToken,
                "Too long.",
                Alias: new string('a', CardService.MaxAliasLength + 1)),
            Json);

        ((int)response.StatusCode).ShouldBe(422);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").GetProperty("Alias")[0].GetString().ShouldContain("64");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.CardRevisions.CountAsync(r => r.CardId == card.Id)).ShouldBe(0);
        (await db.Cards.SingleAsync(c => c.Id == card.Id)).Alias.ShouldBeNull();
    }

    [Test]
    public async Task Limits_endpoint_serves_the_alias_constants()
    {
        using var client = _factory.CreateClient();
        var limits = await client.GetFromJsonAsync<CardLimitsDto>("/api/cards/limits", Json);
        limits.ShouldNotBeNull();
        limits.MaxAliasLength.ShouldBe(CardService.MaxAliasLength);
        limits.MaxAliasWords.ShouldBe(CardService.MaxAliasWords);
    }

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
            Name = $"Api Alias Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = Path.Combine(Path.GetTempPath(), $"antiphon-api-alias-{Guid.NewGuid():N}"),
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
