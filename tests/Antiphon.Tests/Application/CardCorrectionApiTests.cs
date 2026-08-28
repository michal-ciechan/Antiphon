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

    // CARD-0051: PATCH /{id} answers a MoveCardResult, not a bare CardDto. An out-of-repo caller
    // reading the old shape breaks here loudly rather than silently reading `card.status` as
    // undefined — and the response is now the only place a scripted move learns it landed in an
    // active column with nobody on it.
    [Test]
    public async Task A_move_over_http_answers_the_card_and_what_it_did_about_spawning()
    {
        var (board, card) = await SeedAsync("Api move board", "Filed, not started", string.Empty);
        using var client = _factory.CreateClient();
        var columns = await client.GetFromJsonAsync<List<BoardColumnDto>>(
            $"/api/boards/{board.Id}/columns", Json);
        var activeColumn = columns!.Single(c => c.StateKey == "in-progress");

        var response = await client.PatchAsJsonAsync(
            $"/api/cards/{card.Id}",
            new MoveCardRequest(activeColumn.Id, card.ConcurrencyToken, "Belongs here; not starting it."));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<MoveCardResult>(Json))!;
        result.Card.Id.ShouldBe(card.Id);
        result.Card.Status.ShouldBe(CardStatus.InProgress);
        result.Card.OwnerSessionId.ShouldBeNull();
        result.SpawnedSessionId.ShouldBeNull();
        result.SpawnSuppressed.ShouldBeTrue();
        result.Card.AutoDispatchHeldAt.ShouldNotBeNull();
    }

    // The fields assert against the CONSTANTS, never against 300/20000/4000/200: an endpoint that
    // serves numbers of its own is a second source of truth, and the drift only shows up as a
    // caller pre-checking against one ceiling and being refused by another.
    [Test]
    public async Task The_limits_endpoint_serves_the_constants_that_do_the_enforcing()
    {
        using var client = _factory.CreateClient();

        var limits = await client.GetFromJsonAsync<CardLimitsDto>("/api/cards/limits", Json);

        limits.ShouldNotBeNull();
        limits.MaxTitleLength.ShouldBe(CardService.MaxTitleLength);
        limits.MaxDescriptionLength.ShouldBe(CardService.MaxDescriptionLength);
        limits.MaxReasonLength.ShouldBe(CardService.MaxReasonLength);
        limits.MaxActorLength.ShouldBe(CardService.MaxActorLength);
    }

    [Test]
    public async Task Reopen_over_http_returns_the_card_live_again_with_history()
    {
        var (board, card) = await SeedAsync("Api reopen board", "Closed too soon", "Still work to do.");
        using var client = _factory.CreateClient();
        var columns = await client.GetFromJsonAsync<List<BoardColumnDto>>(
            $"/api/boards/{board.Id}/columns", Json);
        var doneColumn = columns!.Single(c => c.StateKey == "done");
        var backlogColumn = columns.Single(c => c.StateKey == "backlog");

        var closed = await client.PatchAsJsonAsync(
            $"/api/cards/{card.Id}",
            new MoveCardRequest(doneColumn.Id, card.ConcurrencyToken, "Filed by mistake."));
        closed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var closedCard = (await closed.Content.ReadFromJsonAsync<MoveCardResult>(Json))!.Card;
        closedCard.Status.ShouldBe(CardStatus.Done);
        closedCard.CompletedAt.ShouldNotBeNull();
        closedCard.TerminalReason.ShouldBe("Filed by mistake.");

        var reopen = await client.PostAsJsonAsync(
            $"/api/cards/{card.Id}/reopen",
            new ReopenCardRequest(closedCard.ConcurrencyToken, "Still open.", ReopenedBy: "operator"));
        reopen.StatusCode.ShouldBe(HttpStatusCode.OK);
        var live = (await reopen.Content.ReadFromJsonAsync<CardDto>(Json))!;
        live.Status.ShouldBe(CardStatus.Backlog);
        live.BoardColumnId.ShouldBe(backlogColumn.Id);
        live.CompletedAt.ShouldBeNull();
        live.TerminalReason.ShouldBeNull();

        var history = await client.GetFromJsonAsync<List<CardRevisionDto>>(
            $"/api/cards/{card.Id}/revisions", Json);
        var reopenRow = history!.Single(r => r.Kind == CardRevisionKind.Reopen);
        reopenRow.FromStatus.ShouldBe(CardStatus.Done);
        reopenRow.ToStatus.ShouldBe(CardStatus.Backlog);
        reopenRow.TerminalReason.ShouldBe("Filed by mistake.");
        reopenRow.CompletedAt.ShouldNotBeNull();
        reopenRow.Reason.ShouldBe("Still open.");
        reopenRow.EditedBy.ShouldBe("operator");
        history.Count(r => r.Kind == CardRevisionKind.Move).ShouldBe(1);
    }

    // A regression here comes back as a 422 "not a card identifier", because /{id} would have
    // swallowed the literal segment.
    [Test]
    public async Task The_limits_route_is_not_swallowed_by_the_identifier_route()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/cards/limits");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("maxTitleLength", out _).ShouldBeTrue();
    }

    [Test]
    public async Task Summary_view_previews_at_a_word_boundary_and_leaves_short_text_whole()
    {
        var longDescription = string.Join(" ", Enumerable.Repeat("abcd", 42));
        var (board, _) = await SeedAsync("Summary preview board", "Long description", longDescription);
        using var client = _factory.CreateClient();

        var summary = await client.GetFromJsonAsync<BoardDetailDto>(
            $"/api/boards/{board.Id}?view=summary", Json);
        var card = summary!.Columns.SelectMany(column => column.Cards).Single();

        // Forty words plus their thirty-nine separators is 199 characters: the next legal cut
        // point nearest the 200-character cap, and never the middle of word 41.
        card.Description.ShouldBe(string.Join(" ", Enumerable.Repeat("abcd", 40)) + "…");
        card.HasMore.ShouldBeTrue();
        card.Sessions.ShouldBeEmpty();

        var shortCard = await SeedAdditionalCardAsync(board.Id, "Short description", "This remains whole.");
        var refreshed = await client.GetFromJsonAsync<BoardDetailDto>(
            $"/api/boards/{board.Id}?view=summary", Json);
        var unchanged = refreshed!.Columns.SelectMany(column => column.Cards).Single(c => c.Id == shortCard.Id);
        unchanged.Description.ShouldBe("This remains whole.");
        unchanged.HasMore.ShouldBeFalse();
    }

    [Test]
    public async Task Full_board_views_remain_wire_identical_and_do_not_emit_summary_metadata()
    {
        var (board, _) = await SeedAsync("Full view board", "As filed", "The complete text.");
        using var client = _factory.CreateClient();

        var noView = await client.GetStringAsync($"/api/boards/{board.Id}");
        var explicitFull = await client.GetStringAsync($"/api/boards/{board.Id}?view=full");

        noView.ShouldBe(explicitFull);
        noView.ShouldNotContain("hasMore");
    }

    [Test]
    public async Task Cards_list_requires_a_filter_and_filters_needs_decision_across_boards()
    {
        var (board, decision) = await SeedAsync("Decision list board", "Need an answer", "Choose one.");
        using var scope = _factory.Services.CreateScope();
        var boards = scope.ServiceProvider.GetRequiredService<BoardService>();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();
        var second = await boards.CreateAsync(new CreateBoardRequest(_projectId, "Second decision board"), CancellationToken.None);
        var ordinary = await cards.CreateAsync(second.Id, new CreateCardRequest(null, "Not waiting", "No decision."), CancellationToken.None);
        var decisionColumn = board.Columns.Single(column => column.CardStatus == CardStatus.NeedsDecision);
        await cards.MoveAsync(decision.Id, new MoveCardRequest(decisionColumn.Id, decision.ConcurrencyToken, "Question for operator."), CancellationToken.None);
        using var client = _factory.CreateClient();

        var unbounded = await client.GetAsync("/api/cards");
        unbounded.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var result = await client.GetFromJsonAsync<CardListDto>("/api/cards?status=NeedsDecision", Json);
        result!.Cards.Select(card => card.Id).ShouldBe([decision.Id]);
        result.Cards.ShouldAllBe(card => card.Status == CardStatus.NeedsDecision);
        result.Cards.ShouldAllBe(card => card.Sessions.Count == 0);
        ordinary.Status.ShouldBe(CardStatus.Backlog);
    }

    [Test]
    public async Task Cards_list_updated_since_uses_the_card_update_timestamp_not_creation_time()
    {
        var (board, created) = await SeedAsync("Updated since board", "Old then revised", "Original.");
        var threshold = DateTime.UtcNow.AddMinutes(-1);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Cards.Where(card => card.Id == created.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(card => card.CreatedAt, DateTime.UtcNow.AddDays(-2)));
            var cards = scope.ServiceProvider.GetRequiredService<CardService>();
            var current = await cards.GetByIdAsync(created.Id, CancellationToken.None);
            await cards.UpdateContentAsync(
                created.Id,
                new UpdateCardContentRequest(current.ConcurrencyToken, "Revised after filing.", Description: "Current."),
                CancellationToken.None);
        }
        using var client = _factory.CreateClient();

        var result = await client.GetFromJsonAsync<CardListDto>(
            $"/api/cards?updatedSince={Uri.EscapeDataString(threshold.ToString("O"))}", Json);

        result!.Cards.Select(card => card.Id).ShouldContain(created.Id);
        result.Cards.Single(card => card.Id == created.Id).CreatedAt.ShouldBeLessThan(threshold);
        result.Cards.Single(card => card.Id == created.Id).UpdatedAt.ShouldBeGreaterThan(threshold);
        board.Id.ShouldNotBe(Guid.Empty);
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

    private async Task<CardDto> SeedAdditionalCardAsync(Guid boardId, string title, string description)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CardService>().CreateAsync(
            boardId,
            new CreateCardRequest(null, title, description),
            CancellationToken.None);
    }
}
