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
[Category("Integration")]
public class CardIdentifierResolutionTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AntiphonWebAppFactory _factory;
    private Guid _projectId;
    private readonly List<Guid> _projectIds = [];
    private readonly List<string> _tempDirs = [];

    public CardIdentifierResolutionTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    [After(Test)]
    public async Task CleanupAsync()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { /* best effort */ }
        }
        _tempDirs.Clear();

        if (_projectIds.Count == 0)
            return;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var boardIds = await db.Boards.Where(b => _projectIds.Contains(b.ProjectId)).Select(b => b.Id).ToListAsync();
        var cardIds = await db.Cards.Where(c => boardIds.Contains(c.BoardId)).Select(c => c.Id).ToListAsync();
        await db.AgentSessions.Where(s => s.CardId != null && cardIds.Contains(s.CardId.Value)).ExecuteDeleteAsync();
        await db.AgentTasks.Where(t => t.CardId != null && cardIds.Contains(t.CardId.Value)).ExecuteDeleteAsync();
        await db.Agents.Where(a => a.BoardId != null && boardIds.Contains(a.BoardId.Value)).ExecuteDeleteAsync();
        await db.CardRevisions.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => _projectIds.Contains(p.Id)).ExecuteDeleteAsync();
        _projectIds.Clear();
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
    public async Task The_same_identifier_on_two_boards_is_a_409_naming_every_candidate()
    {
        var (boardA, _, _) = await SeedAsync("Ambiguous board A");
        var boardB = await CreateBoardAsync("Ambiguous board B");
        var shared = await NextUnusedIdentifierAsync();
        var idA = await SeedCardWithIdentifierAsync(boardA.Id, "Same number, board A", shared);
        var idB = await SeedCardWithIdentifierAsync(boardB.Id, "Same number, board B", shared);
        using var scope = _factory.Services.CreateScope();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();

        var ex = await Should.ThrowAsync<ConflictException>(() =>
            cards.ResolveCardIdAsync(shared, CancellationToken.None));

        ex.Code.ShouldBe("card_identifier_ambiguous");
        ex.Message.ShouldContain(shared);
        ex.Message.ShouldContain(boardA.Name);
        ex.Message.ShouldContain(boardB.Name);
        ex.Message.ShouldContain(idA.ToString());
        ex.Message.ShouldContain(idB.ToString());
        ex.Message.ShouldContain("Same number, board A");
        ex.Message.ShouldContain("Same number, board B");
        ex.Extensions.ShouldNotBeNull();
        ex.Extensions!.ShouldContainKey("candidates");

        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/cards/{shared}");
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().ShouldBe("card_identifier_ambiguous");
        var candidates = problem.GetProperty("candidates");
        candidates.GetArrayLength().ShouldBe(2);
        foreach (var candidate in candidates.EnumerateArray())
        {
            candidate.GetProperty("id").GetGuid().ShouldBeOneOf(idA, idB);
            candidate.GetProperty("boardName").GetString().ShouldBeOneOf(boardA.Name, boardB.Name);
            candidate.GetProperty("status").GetString().ShouldNotBeNullOrWhiteSpace();
            candidate.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Test]
    public async Task An_explicit_board_scopes_the_identifier_to_that_board()
    {
        var (boardA, _, _) = await SeedAsync("Fence board A");
        var boardB = await CreateBoardAsync("Fence board B");
        var shared = await NextUnusedIdentifierAsync();
        var idA = await SeedCardWithIdentifierAsync(boardA.Id, "Fenced A", shared);
        var idB = await SeedCardWithIdentifierAsync(boardB.Id, "Fenced B", shared);
        using var scope = _factory.Services.CreateScope();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();

        (await cards.ResolveCardIdAsync(
            shared, new CardScopeContext(boardB.Id, null, null, null), CancellationToken.None))
            .ShouldBe(idB);
        (await cards.ResolveCardIdAsync(
            shared, new CardScopeContext(boardA.Id, null, null, null), CancellationToken.None))
            .ShouldBe(idA);

        using var client = _factory.CreateClient();
        var fromB = await client.GetFromJsonAsync<CardDto>(
            $"/api/cards/{shared}?boardId={boardB.Id}", Json);
        fromB!.Id.ShouldBe(idB);
        var fromA = await client.GetFromJsonAsync<CardDto>(
            $"/api/cards/{shared}?boardId={boardA.Id}", Json);
        fromA!.Id.ShouldBe(idA);
    }

    [Test]
    public async Task An_explicit_board_that_lacks_the_identifier_is_a_404_that_says_where_it_lives()
    {
        var (boardA, _, _) = await SeedAsync("Holder board A");
        var boardB = await CreateBoardAsync("Holder board B");
        var boardC = await CreateBoardAsync("Empty fence board C");
        var shared = await NextUnusedIdentifierAsync();
        var idA = await SeedCardWithIdentifierAsync(boardA.Id, "Lives on A", shared);
        var idB = await SeedCardWithIdentifierAsync(boardB.Id, "Lives on B", shared);
        using var scope = _factory.Services.CreateScope();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();

        var ex = await Should.ThrowAsync<NotFoundException>(() =>
            cards.ResolveCardIdAsync(
                shared, new CardScopeContext(boardC.Id, null, null, null), CancellationToken.None));
        ex.Message.ShouldContain(boardC.Name);
        ex.Message.ShouldContain(boardA.Name);
        ex.Message.ShouldContain(boardB.Name);
        ex.Message.ShouldContain(idA.ToString());
        ex.Message.ShouldContain(idB.ToString());

        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/cards/{shared}?boardId={boardC.Id}");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var detail = problem.GetProperty("detail").GetString()!;
        detail.ShouldContain(boardC.Name);
        detail.ShouldContain(boardA.Name);
        detail.ShouldContain(boardB.Name);
    }

    [Test]
    public async Task A_delegates_token_resolves_the_identifier_on_its_own_cards_board()
    {
        var (boardA, _, _) = await SeedAsync("Token board A");
        var boardB = await CreateBoardAsync("Token board B");
        var shared = await NextUnusedIdentifierAsync();
        var idA = await SeedCardWithIdentifierAsync(boardA.Id, "Caller's card", shared);
        await SeedCardWithIdentifierAsync(boardB.Id, "Decoy card", shared);
        var token = await SeedDelegateTokenAsync(idA);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", token);
        var fetched = await client.GetFromJsonAsync<CardDto>($"/api/cards/{shared}", Json);
        fetched!.Id.ShouldBe(idA);
    }

    [Test]
    public async Task A_checkout_under_a_projects_repository_resolves_the_identifier_on_that_projects_boards()
    {
        var rootA = Directory.CreateTempSubdirectory("card0218-a").FullName;
        var rootB = Directory.CreateTempSubdirectory("card0218-b").FullName;
        _tempDirs.Add(rootA);
        _tempDirs.Add(rootB);
        var nested = Path.Combine(rootA, "nested");
        Directory.CreateDirectory(nested);

        var (boardA, _, _) = await SeedAsync("Repo board A");
        await SetProjectLocalPathAsync(_projectId, rootA);
        var projectB = await CreateExtraProjectAsync(rootB);
        var boardB = await CreateBoardOnAsync(projectB, "Repo board B");
        var shared = await NextUnusedIdentifierAsync();
        var idA = await SeedCardWithIdentifierAsync(boardA.Id, "Repo A card", shared);
        await SeedCardWithIdentifierAsync(boardB.Id, "Repo B card", shared);

        var mixedCwd = nested.Replace('\\', '/');
        mixedCwd = char.IsUpper(mixedCwd[0])
            ? char.ToLowerInvariant(mixedCwd[0]) + mixedCwd[1..]
            : char.ToUpperInvariant(mixedCwd[0]) + mixedCwd[1..];

        using var client = _factory.CreateClient();
        var fetched = await client.GetFromJsonAsync<CardDto>(
            $"/api/cards/{shared}?cwd={Uri.EscapeDataString(mixedCwd)}", Json);
        fetched!.Id.ShouldBe(idA);
    }

    [Test]
    public async Task A_stale_token_does_not_turn_a_card_read_into_a_403()
    {
        var (_, card, identifier) = await SeedAsync("Stale token board");
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", "garbage-not-a-token");
        var response = await client.GetAsync($"/api/cards/{identifier}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var fetched = await response.Content.ReadFromJsonAsync<CardDto>(Json);
        fetched!.Id.ShouldBe(card.Id);
    }

    [Test]
    public async Task A_guid_resolves_regardless_of_scope()
    {
        var (boardA, cardA, _) = await SeedAsync("Guid scope board A");
        var boardB = await CreateBoardAsync("Guid scope board B");
        using var scope = _factory.Services.CreateScope();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();

        (await cards.ResolveCardIdAsync(
            cardA.Id.ToString(),
            new CardScopeContext(boardB.Id, null, null, null),
            CancellationToken.None)).ShouldBe(cardA.Id);

        using var client = _factory.CreateClient();
        var fetched = await client.GetFromJsonAsync<CardDto>(
            $"/api/cards/{cardA.Id}?boardId={boardB.Id}", Json);
        fetched!.Id.ShouldBe(cardA.Id);
        fetched.BoardId.ShouldBe(boardA.Id);
    }

    [Test]
    public async Task The_card_api_and_the_task_binder_answer_the_same_card()
    {
        var (boardA, _, _) = await SeedAsync("Parity board A");
        var boardB = await CreateBoardAsync("Parity board B");
        var shared = await NextUnusedIdentifierAsync();
        var idA = await SeedCardWithIdentifierAsync(boardA.Id, "Parity A", shared);
        await SeedCardWithIdentifierAsync(boardB.Id, "Parity B", shared);

        using var scope = _factory.Services.CreateScope();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ctx = new CardScopeContext(null, idA, null, @"C:\card0218-unused");

        var viaApi = await cards.ResolveCardIdAsync(shared, ctx, CancellationToken.None);
        var viaBinder = await AgentTaskCardBinder.BindAsync(
            db,
            shared,
            new AgentTaskCardBinder.Context(
                AgentTaskRole.Custom,
                "Parity goal",
                idA,
                null,
                null,
                @"C:\card0218-unused"),
            CancellationToken.None);

        viaApi.ShouldBe(idA);
        viaBinder.CardId.ShouldBe(idA);
        viaApi.ShouldBe(viaBinder.CardId!.Value);
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
        _projectIds.Add(project.Id);

        return await CreateBoardAsync(boardName);
    }

    private async Task<Guid> CreateExtraProjectAsync(string localRepositoryPath)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Card Identifier Extra {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = localRepositoryPath,
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        _projectIds.Add(project.Id);
        return project.Id;
    }

    private async Task SetProjectLocalPathAsync(Guid projectId, string localRepositoryPath)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Projects.Where(p => p.Id == projectId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LocalRepositoryPath, localRepositoryPath));
    }

    private async Task<string> SeedDelegateTokenAsync(Guid cardId)
    {
        var token = Guid.NewGuid().ToString("N");
        var id = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "CARD-0218 token caller",
            Goal = "Scope a card read.",
            WorkingDirectory = @"C:\card0218-token",
            CardId = cardId,
            TokenHash = AgentTaskService.HashToken(token),
            Kind = AgentTaskKind.Orchestrator,
            Status = AgentTaskStatus.Working,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return token;
    }

    private async Task<BoardDetailDto> CreateBoardAsync(string boardName) =>
        await CreateBoardOnAsync(_projectId, boardName);

    private async Task<BoardDetailDto> CreateBoardOnAsync(Guid projectId, string boardName)
    {
        using var scope = _factory.Services.CreateScope();
        var boards = scope.ServiceProvider.GetRequiredService<BoardService>();
        return await boards.CreateAsync(
            new CreateBoardRequest(projectId, $"{boardName} {Guid.NewGuid():N}"), CancellationToken.None);
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
