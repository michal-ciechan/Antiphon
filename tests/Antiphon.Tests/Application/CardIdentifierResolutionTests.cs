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
/// CARD-0051 slice 1: a card is addressable by the name everyone actually uses for it.
/// </summary>
/// <remarks>
/// Every test seeds an identifier that is GLOBALLY unused, not just unused on its own board. The
/// assembly shares one Postgres and every board's first card is CARD-0001, so a test that resolved
/// the identifier its board handed out would be asserting "no other suite has a first card right
/// now" — which is exactly the ambiguity the 409 arm exists for, arriving as a random failure.
/// </remarks>
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
public class CardIdentifierResolutionTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AntiphonWebAppFactory _factory;
    private Guid _projectId;

    public CardIdentifierResolutionTests(AntiphonWebAppFactory factory) => _factory = factory;

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
        await db.ExternalIssueRefs.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.CardRevisions.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => p.Id == _projectId).ExecuteDeleteAsync();
        _projectId = Guid.Empty;
    }

    [Test]
    public async Task A_guid_resolves_to_itself_and_an_unknown_guid_is_a_404()
    {
        var (_, card, _) = await SeedAsync("Guid passthrough board");
        using var scope = _factory.Services.CreateScope();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();

        (await cards.ResolveCardIdAsync(card.Id.ToString(), CancellationToken.None)).ShouldBe(card.Id);

        await Should.ThrowAsync<NotFoundException>(() =>
            cards.ResolveCardIdAsync(Guid.NewGuid().ToString(), CancellationToken.None));
    }

    // The four forms client/src/shared/cardIdentifier.ts already parses, plus the two casings a
    // human types. All of them name ONE card, so all of them must reach it.
    [Test]
    public async Task Every_form_a_human_writes_the_identifier_in_resolves_to_the_same_card()
    {
        var (_, card, identifier) = await SeedAsync("Identifier form board");
        var number = int.Parse(identifier["CARD-".Length..]);
        using var scope = _factory.Services.CreateScope();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();

        string[] forms =
        [
            identifier,                       // CARD-0051
            identifier.ToLowerInvariant(),    // card-0051
            $"Card-{number:0000}",            // Card-0051
            $"CARD-{number}",                 // CARD-51  (leading zeros dropped)
            $"card-{number}",                 // card-51
            $"#{number}",                     // #51
            number.ToString(),                // 51
            $"  {identifier}  ",              // whatever the shell left on it
        ];

        foreach (var form in forms)
        {
            (await cards.ResolveCardIdAsync(form, CancellationToken.None))
                .ShouldBe(card.Id, $"'{form}' names {identifier}");
        }
    }

    // A board synced from a foreign tracker keeps its own identifiers; they are not CARD-nnnn and
    // must still resolve by the exact text that tracker hands out.
    [Test]
    public async Task A_foreign_tracker_identifier_resolves_by_its_own_exact_form()
    {
        var (board, _, _) = await SeedAsync("Foreign tracker board");
        var foreign = await NextUnusedForeignIdentifierAsync();
        var cardId = await SeedCardWithIdentifierAsync(board.Id, "Filed in someone else's tracker", foreign);
        using var scope = _factory.Services.CreateScope();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();

        (await cards.ResolveCardIdAsync(foreign, CancellationToken.None)).ShouldBe(cardId);
        (await cards.ResolveCardIdAsync(foreign.ToLowerInvariant(), CancellationToken.None)).ShouldBe(cardId);
    }

    // The client's search box matches identifier PREFIXES so incremental typing narrows. An API
    // that did the same would let "5" address CARD-0051 on a route that writes.
    [Test]
    public async Task A_prefix_of_an_identifier_is_not_a_match()
    {
        var (_, card, identifier) = await SeedAsync("Prefix board");
        var number = int.Parse(identifier["CARD-".Length..]);
        var prefix = number.ToString()[..^1];
        using var scope = _factory.Services.CreateScope();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();

        var resolved = await Record(() => cards.ResolveCardIdAsync(prefix, CancellationToken.None));

        // Either nothing holds that exact number (404) or some other card does — never THIS one.
        resolved.Id.ShouldNotBe(card.Id);
    }

    [Test]
    public async Task An_identifier_no_card_holds_is_a_404()
    {
        await SeedAsync("Missing identifier board");
        var unused = await NextUnusedIdentifierAsync();
        using var scope = _factory.Services.CreateScope();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();

        await Should.ThrowAsync<NotFoundException>(() =>
            cards.ResolveCardIdAsync(unused, CancellationToken.None));
    }

    // Identifier is unique PER BOARD (IX_Cards_BoardId_Identifier). Two boards can each hold a
    // CARD-0001, and a resolver that took the first row would silently address the wrong card.
    [Test]
    public async Task The_same_identifier_on_two_boards_is_a_409_naming_the_way_out()
    {
        var (boardA, _, _) = await SeedAsync("Ambiguous board A");
        var boardB = await CreateBoardAsync("Ambiguous board B");
        var shared = await NextUnusedIdentifierAsync();
        await SeedCardWithIdentifierAsync(boardA.Id, "Same number, board A", shared);
        await SeedCardWithIdentifierAsync(boardB.Id, "Same number, board B", shared);
        using var scope = _factory.Services.CreateScope();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();

        var ex = await Should.ThrowAsync<ConflictException>(() =>
            cards.ResolveCardIdAsync(shared, CancellationToken.None));

        ex.Message.ShouldContain(shared);
        ex.Message.ShouldContain("guid");
    }

    // CARD-0175 T3. Live on 2026-08-24 this was a 409: an imported card was literally named "#5"
    // and `#5` is also the entry form of CARD-0005, so `card.ps1 get '#5'` was broken for every
    // N <= 13 on that board and the set grew with every import. Imported cards now get their own
    // CARD-nnnn and `#N` means CARD-000N and nothing else.
    [Test]
    public async Task Hash_N_resolves_to_the_card_not_the_import()
    {
        var (board, _, _) = await SeedAsync("Hash collision board");
        var identifier = await NextUnusedIdentifierAsync();
        var number = int.Parse(identifier["CARD-".Length..]);
        var manualId = await SeedCardWithIdentifierAsync(board.Id, "The real card", identifier);
        // An import of GitHub issue #<same number> onto the same board.
        var importedId = await SeedCardWithIdentifierAsync(
            board.Id, "GitHub import", await NextUnusedIdentifierAsync());
        await LinkToTrackerAsync(importedId, $"acme/app#{number}", $"#{number}");

        using var scope = _factory.Services.CreateScope();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();

        (await cards.ResolveCardIdAsync($"#{number}", CancellationToken.None)).ShouldBe(manualId);
        (await cards.ResolveCardIdAsync(identifier, CancellationToken.None)).ShouldBe(manualId);
        (await cards.ResolveCardIdAsync(number.ToString(), CancellationToken.None)).ShouldBe(manualId);
    }

    // CARD-0175 T4. A foreign tracker's key used to BE the card's identifier; now it lives on the
    // external ref, so the resolver's PREFIX-digits arm has to reach through it or `card.ps1 get
    // ANT-12` stops working on a Jira/Linear board.
    [Test]
    public async Task Foreign_key_resolves_through_the_external_ref()
    {
        var (board, _, _) = await SeedAsync("Foreign key board");
        var cardId = await SeedCardWithIdentifierAsync(
            board.Id, "Linear import", await NextUnusedIdentifierAsync());
        var key = $"ANT-{Random.Shared.Next(100_000, 999_999)}";
        await LinkToTrackerAsync(cardId, $"lin-{key}", key);

        using var scope = _factory.Services.CreateScope();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();

        (await cards.ResolveCardIdAsync(key, CancellationToken.None)).ShouldBe(cardId);
        (await cards.ResolveCardIdAsync(key.ToLowerInvariant(), CancellationToken.None)).ShouldBe(cardId);
    }

    // Not a 404: nothing was looked for. The message names the forms, and a literal route segment
    // that ever stopped outranking {id} fails loudly here rather than reporting "no such card".
    [Test]
    public async Task Input_that_is_not_identifier_shaped_is_a_422_that_names_the_forms()
    {
        await SeedAsync("Junk board");
        using var scope = _factory.Services.CreateScope();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();

        foreach (var junk in new[] { "limits", "not an id", "", "   ", "%%%", "card-" })
        {
            var ex = await Should.ThrowAsync<ValidationException>(
                () => cards.ResolveCardIdAsync(junk, CancellationToken.None),
                $"'{junk}' is not an identifier");
            ex.Errors.Values.SelectMany(e => e).ShouldContain(m => m.Contains("CARD-0051"));
        }
    }

    [Test]
    public async Task Reading_and_correcting_a_card_over_http_both_take_the_identifier()
    {
        var (_, card, identifier) = await SeedAsync("Http identifier board", "As filed");
        var number = int.Parse(identifier["CARD-".Length..]);
        using var client = _factory.CreateClient();

        var read = await client.GetAsync($"/api/cards/{identifier}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        var fetched = (await read.Content.ReadFromJsonAsync<CardDto>(Json))!;
        fetched.Id.ShouldBe(card.Id);
        fetched.Title.ShouldBe("As filed");

        // The bare number too - the UI renders CARD-0051 as #51, so that is the form that gets typed.
        var shortForm = await client.GetFromJsonAsync<CardDto>($"/api/cards/{number}", Json);
        shortForm!.Id.ShouldBe(card.Id);

        var patch = await client.PatchAsJsonAsync(
            $"/api/cards/{identifier}/content",
            new UpdateCardContentRequest(fetched.ConcurrencyToken, "Corrected by identifier.", Title: "As corrected"));
        patch.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await patch.Content.ReadFromJsonAsync<CardDto>(Json))!.Title.ShouldBe("As corrected");

        var revisions = await client.GetFromJsonAsync<List<CardRevisionDto>>(
            $"/api/cards/{identifier}/revisions", Json);
        revisions!.Count.ShouldBe(1);
    }

    [Test]
    public async Task Garbage_in_the_id_segment_is_a_422_over_http_not_a_500()
    {
        await SeedAsync("Http junk board");
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/cards/not-a-card");

        ((int)response.StatusCode).ShouldBe(422);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").EnumerateObject().ShouldNotBeEmpty();
    }

    [Test]
    public async Task The_columns_endpoint_returns_the_boards_shape_and_none_of_its_cards()
    {
        var (board, _, _) = await SeedAsync("Columns board", "On the board");
        using var client = _factory.CreateClient();

        var columns = await client.GetFromJsonAsync<List<BoardColumnDto>>(
            $"/api/boards/{board.Id}/columns", Json);

        columns.ShouldNotBeNull();
        columns.Select(c => c.StateKey).ShouldBe(board.Columns.Select(c => c.StateKey));
        columns.Select(c => c.Id).ShouldBe(board.Columns.Select(c => c.Id));
        columns.ShouldAllBe(c => c.Cards.Count == 0);
        // The point of the endpoint: enough to pick a move target without downloading the board.
        columns.ShouldContain(c => c.IsTerminal);
        columns.ShouldContain(c => c.IsActive);
    }

    [Test]
    public async Task An_unknown_board_has_no_columns_and_says_so()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/boards/{Guid.NewGuid()}/columns");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<(BoardDetailDto Board, CardDto Card, string Identifier)> SeedAsync(
        string boardName, string cardTitle = "Seed card")
    {
        var board = await CreateProjectAndBoardAsync(boardName);
        var identifier = await NextUnusedIdentifierAsync();

        using var scope = _factory.Services.CreateScope();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var created = await cards.CreateAsync(
            board.Id, new CreateCardRequest(null, cardTitle), CancellationToken.None);
        await db.Cards.Where(c => c.Id == created.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Identifier, identifier));

        return (board, created with { Identifier = identifier }, identifier);
    }

    private async Task<BoardDetailDto> CreateProjectAndBoardAsync(string boardName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Card Identifier Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = Path.Combine(Path.GetTempPath(), $"antiphon-card-ids-{Guid.NewGuid():N}"),
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        _projectId = project.Id;

        return await CreateBoardAsync(boardName);
    }

    private async Task<BoardDetailDto> CreateBoardAsync(string boardName)
    {
        using var scope = _factory.Services.CreateScope();
        var boards = scope.ServiceProvider.GetRequiredService<BoardService>();
        return await boards.CreateAsync(
            new CreateBoardRequest(_projectId, $"{boardName} {Guid.NewGuid():N}"), CancellationToken.None);
    }

    private async Task<Guid> SeedCardWithIdentifierAsync(Guid boardId, string title, string identifier)
    {
        using var scope = _factory.Services.CreateScope();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var created = await cards.CreateAsync(
            boardId, new CreateCardRequest(null, title), CancellationToken.None);
        await db.Cards.Where(c => c.Id == created.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Identifier, identifier));
        return created.Id;
    }

    /// <summary>Links a card to a tracker issue, the way an import leaves it (CARD-0175).</summary>
    private async Task LinkToTrackerAsync(Guid cardId, string externalId, string externalKey)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ExternalIssueRefs.Add(new ExternalIssueRef
        {
            Id = Guid.NewGuid(),
            CardId = cardId,
            TrackerKind = TrackerKind.GitHubIssues,
            ExternalId = externalId,
            ExternalKey = externalKey,
            Url = $"https://github.test/{externalId.Replace('#', '/')}",
            RawPayloadJson = "{}",
            LastSyncedAt = DateTime.UtcNow,
            Origin = ExternalIssueOrigin.ExternalImport,
            LastKnownExternalState = "open"
        });
        await db.SaveChangesAsync();
    }

    /// <summary>A <c>CARD-nnnn</c> no row in the shared database holds. See the class remark.</summary>
    private async Task<string> NextUnusedIdentifierAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var candidate = $"CARD-{Random.Shared.Next(4_000, 9_999):0000}";
            if (!await db.Cards.AnyAsync(c => c.Identifier == candidate))
                return candidate;
        }

        throw new InvalidOperationException("No unused CARD-nnnn identifier available.");
    }

    private async Task<string> NextUnusedForeignIdentifierAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var candidate = $"PROJ-{Random.Shared.Next(1_000, 9_999)}";
            if (!await db.Cards.AnyAsync(c => c.Identifier == candidate))
                return candidate;
        }

        throw new InvalidOperationException("No unused foreign identifier available.");
    }

    private static async Task<(Guid? Id, Exception? Error)> Record(Func<Task<Guid>> act)
    {
        try
        {
            return (await act(), null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }
}
