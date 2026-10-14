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
/// CARD-0100 V-2..V-5. The relationship routes over real HTTP: add, refuse, resolve, remove, and
/// the open-blocker summary on the card and list routes. Every status change of a blocker goes
/// through its own HTTP route, so "no write on the blocked card" is read back after a real write.
/// </summary>
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
[Category("Integration")]
public class CardRelationshipApiTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AntiphonWebAppFactory _factory;
    private Guid _projectId;

    public CardRelationshipApiTests(AntiphonWebAppFactory factory) => _factory = factory;

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
        await db.CardRelationships
            .Where(r => cardIds.Contains(r.CardId) || cardIds.Contains(r.RelatedCardId))
            .ExecuteDeleteAsync();
        // A Done move writes a CardWorktreeCleanup whose Restrict FK names the Done revision.
        await db.CardWorktreeCleanups.Where(c => cardIds.Contains(c.CardId)).ExecuteDeleteAsync();
        await db.CardRevisions.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => p.Id == _projectId).ExecuteDeleteAsync();
        _projectId = Guid.Empty;
    }

    [Test]
    public async Task Add_blocker_by_identifier_returns_201_rotates_the_token_and_writes_RelationshipAdded()
    {
        var board = await SeedBoardAsync("c100 add");
        var blocked = await CreateCardAsync(board.Id, "blocked");
        var blocker = await CreateCardAsync(board.Id, "blocker");
        blocked.Identifier.ShouldBe("CARD-0001");
        blocker.Identifier.ShouldBe("CARD-0002");
        using var client = _factory.CreateClient();
        var before = await GetCardAsync(client, blocked.Id);

        var response = await PostAddAsync(client, blocked.Id, new
        {
            concurrencyToken = before.ConcurrencyToken,
            relatedCard = "CARD-0002",
            kind = "BlockedBy",
            reason = "needs the schema first",
            createdBy = "operator",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location!.ToString().ShouldBe($"/api/cards/{blocked.Id}/relationships");
        var created = (await response.Content.ReadFromJsonAsync<CardRelationshipDto>(Json))!;
        created.Kind.ShouldBe(CardRelationshipKind.BlockedBy);
        created.CardId.ShouldBe(blocked.Id);
        created.RelatedCardId.ShouldBe(blocker.Id);
        created.RelatedIdentifier.ShouldBe("CARD-0002");
        created.RelatedTitle.ShouldBe("blocker");
        created.RelatedBoardId.ShouldBe(board.Id);
        created.RelatedStatus.ShouldBe(CardStatus.Backlog);
        created.RelatedArchivedAt.ShouldBeNull();
        created.Resolved.ShouldBeFalse();
        created.Reason.ShouldBe("needs the schema first");
        created.CreatedBy.ShouldBe("operator");

        var after = await GetCardAsync(client, blocked.Id);
        after.ConcurrencyToken.ShouldNotBe(before.ConcurrencyToken);
        created.CardConcurrencyToken.ShouldBe(after.ConcurrencyToken);
        after.RevisionCount.ShouldBe(1);
        after.UpdatedAt.ShouldBeGreaterThan(before.UpdatedAt);

        var revisions = (await client.GetFromJsonAsync<List<CardRevisionDto>>(
            $"/api/cards/{blocked.Id}/revisions", Json))!;
        revisions.Count.ShouldBe(1);
        revisions[0].Kind.ShouldBe(CardRevisionKind.RelationshipAdded);
        revisions[0].RelatedCardId.ShouldBe(blocker.Id);
        revisions[0].RelatedCardIdentifier.ShouldBe("CARD-0002");
        revisions[0].RelationshipKind.ShouldBe(CardRelationshipKind.BlockedBy);
        revisions[0].Reason.ShouldBe("needs the schema first");
        revisions[0].EditedBy.ShouldBe("operator");
    }

    [Test]
    public async Task Adding_a_blocker_leaves_the_blockers_token_and_revisions_untouched()
    {
        var board = await SeedBoardAsync("c100 blocker untouched");
        var blocked = await CreateCardAsync(board.Id, "blocked");
        var blocker = await CreateCardAsync(board.Id, "blocker");
        using var client = _factory.CreateClient();
        var blockerBefore = await GetCardAsync(client, blocker.Id);

        (await AddAsync(client, blocked.Id, "CARD-0002")).StatusCode.ShouldBe(HttpStatusCode.Created);

        var blockerAfter = await GetCardAsync(client, blocker.Id);
        blockerAfter.ConcurrencyToken.ShouldBe(blockerBefore.ConcurrencyToken);
        blockerAfter.RevisionCount.ShouldBe(0);
        blockerAfter.UpdatedAt.ShouldBe(blockerBefore.UpdatedAt);
    }

    [Test]
    [Arguments("empty")]
    [Arguments("stale")]
    public async Task An_empty_or_stale_token_is_refused(string variant)
    {
        var board = await SeedBoardAsync("c100 token " + variant);
        var blocked = await CreateCardAsync(board.Id, "blocked");
        await CreateCardAsync(board.Id, "blocker");
        using var client = _factory.CreateClient();
        var token = Guid.Empty;
        if (variant == "stale")
        {
            token = blocked.ConcurrencyToken;
            var edit = await client.PatchAsJsonAsync(
                $"/api/cards/{blocked.Id}/content",
                new UpdateCardContentRequest(token, "Unrelated edit.", Title: "blocked, renamed"));
            edit.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var response = await PostAddAsync(client, blocked.Id,
            new { concurrencyToken = token, relatedCard = "CARD-0002", kind = "BlockedBy" });

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (variant == "empty")
        {
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            problem.GetProperty("errors").TryGetProperty("concurrencyToken", out _).ShouldBeTrue();
        }
        else
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            problem.GetProperty("code").GetString().ShouldBe("conflict");
        }

        (await GetRelationshipsAsync(client, blocked.Id)).BlockedBy.ShouldBeEmpty();
    }

    [Test]
    [Arguments("kind")]
    [Arguments("reason")]
    [Arguments("createdBy")]
    public async Task Unknown_kind_and_over_length_text_are_422(string field)
    {
        var board = await SeedBoardAsync("c100 422 " + field);
        var blocked = await CreateCardAsync(board.Id, "blocked");
        await CreateCardAsync(board.Id, "blocker");
        using var client = _factory.CreateClient();

        var body = new Dictionary<string, object?>
        {
            ["concurrencyToken"] = blocked.ConcurrencyToken,
            ["relatedCard"] = "CARD-0002",
            ["kind"] = field == "kind" ? "Blocks" : "BlockedBy",
        };
        if (field == "reason")
            body["reason"] = new string('r', CardService.MaxReasonLength + 1);
        if (field == "createdBy")
            body["createdBy"] = new string('c', CardService.MaxActorLength + 1);

        var response = await PostAddAsync(client, blocked.Id, body);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().ShouldBe("validation_failed");
        problem.GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue();
        (await GetRelationshipsAsync(client, blocked.Id)).BlockedBy.ShouldBeEmpty();
    }

    [Test]
    public async Task Duplicate_pair_is_409_card_relationship_exists()
    {
        var board = await SeedBoardAsync("c100 duplicate");
        var blocked = await CreateCardAsync(board.Id, "blocked");
        await CreateCardAsync(board.Id, "blocker");
        using var client = _factory.CreateClient();

        (await AddAsync(client, blocked.Id, "CARD-0002")).StatusCode.ShouldBe(HttpStatusCode.Created);
        var second = await AddAsync(client, blocked.Id, "CARD-0002");

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().ShouldBe(CardService.CardRelationshipExistsCode);
        (await GetRelationshipsAsync(client, blocked.Id)).BlockedBy.Count.ShouldBe(1);
    }

    // Rows "A<-B" mean card A is blocked by card B. The diamond row reaches R twice and is not a cycle.
    [Test]
    [Arguments("self")]
    [Arguments("two_cycle")]
    [Arguments("three_cycle")]
    [Arguments("diamond")]
    [Arguments("archived_blocker")]
    [Arguments("unknown_card")]
    public async Task Relationship_write_refusal_table(string variant)
    {
        var board = await SeedBoardAsync("c100 refusal " + variant);
        var cards = new List<CardDto>();
        for (var i = 0; i < 5; i++)
            cards.Add(await CreateCardAsync(board.Id, "card " + (i + 1)));
        using var client = _factory.CreateClient();
        var a = cards[0];

        Guid blockedId;
        HttpResponseMessage response;
        switch (variant)
        {
            case "self":
                blockedId = a.Id;
                response = await AddAsync(client, a.Id, "CARD-0001");
                response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
                (await Problem(response)).GetProperty("errors").TryGetProperty("relatedCard", out _).ShouldBeTrue();
                break;
            case "two_cycle":
                await AddCreatedAsync(client, cards[0].Id, "CARD-0002");
                blockedId = cards[1].Id;
                response = await AddAsync(client, cards[1].Id, "CARD-0001");
                response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
                (await Problem(response)).GetProperty("code").GetString().ShouldBe(CardService.CardRelationshipCycleCode);
                break;
            case "three_cycle":
                await AddCreatedAsync(client, cards[0].Id, "CARD-0002");
                await AddCreatedAsync(client, cards[1].Id, "CARD-0003");
                blockedId = cards[2].Id;
                response = await AddAsync(client, cards[2].Id, "CARD-0001");
                response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
                var cycle = await Problem(response);
                cycle.GetProperty("code").GetString().ShouldBe(CardService.CardRelationshipCycleCode);
                var detail = cycle.GetProperty("detail").GetString().ShouldNotBeNull();
                detail.ShouldContain("CARD-0001");
                detail.ShouldContain("CARD-0002");
                detail.ShouldContain("CARD-0003");
                break;
            case "diamond":
                // X=1, P=2, Q=3, R=4, W=5: X<-P, X<-Q, P<-R, Q<-R, then W<-X.
                await AddCreatedAsync(client, cards[0].Id, "CARD-0002");
                await AddCreatedAsync(client, cards[0].Id, "CARD-0003");
                await AddCreatedAsync(client, cards[1].Id, "CARD-0004");
                await AddCreatedAsync(client, cards[2].Id, "CARD-0004");
                blockedId = cards[4].Id;
                response = await AddAsync(client, cards[4].Id, "CARD-0001");
                response.StatusCode.ShouldBe(HttpStatusCode.Created);
                (await GetRelationshipsAsync(client, blockedId)).BlockedBy.Count.ShouldBe(1);
                return;
            case "archived_blocker":
                var blocker = await GetCardAsync(client, cards[1].Id);
                (await client.PostAsJsonAsync($"/api/cards/{blocker.Id}/archive",
                    new ArchiveCardRequest(blocker.ConcurrencyToken, "gone", "operator")))
                    .StatusCode.ShouldBe(HttpStatusCode.OK);
                blockedId = a.Id;
                response = await AddAsync(client, a.Id, "CARD-0002");
                response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
                var archived = await Problem(response);
                archived.GetProperty("code").GetString().ShouldBe("conflict");
                archived.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("archived");
                break;
            case "unknown_card":
                blockedId = a.Id;
                response = await AddAsync(client, a.Id, "CARD-9999");
                response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
                break;
            default:
                throw new InvalidOperationException(variant);
        }

        // In every refused row the blocked card owned no row before the write and owns none after it.
        (await GetRelationshipsAsync(client, blockedId)).BlockedBy.ShouldBeEmpty();
        (await GetCardAsync(client, blockedId)).RevisionCount.ShouldBe(0);
    }

    [Test]
    public async Task An_archived_blocked_card_refuses_add_and_remove()
    {
        var board = await SeedBoardAsync("c100 archived blocked");
        var blocked = await CreateCardAsync(board.Id, "blocked");
        await CreateCardAsync(board.Id, "blocker");
        await CreateCardAsync(board.Id, "second blocker");
        using var client = _factory.CreateClient();
        var row = await AddCreatedAsync(client, blocked.Id, "CARD-0002");
        var current = await GetCardAsync(client, blocked.Id);
        var archive = await client.PostAsJsonAsync($"/api/cards/{blocked.Id}/archive",
            new ArchiveCardRequest(current.ConcurrencyToken, "parked", "operator"));
        archive.StatusCode.ShouldBe(HttpStatusCode.OK);
        var archived = (await archive.Content.ReadFromJsonAsync<CardDto>(Json))!;

        var add = await PostAddAsync(client, blocked.Id,
            new { concurrencyToken = archived.ConcurrencyToken, relatedCard = "CARD-0003", kind = "BlockedBy" });
        add.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var addProblem = await Problem(add);
        addProblem.GetProperty("code").GetString().ShouldBe("conflict");
        addProblem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("archived");

        var remove = await client.PostAsJsonAsync(
            $"/api/cards/{blocked.Id}/relationships/{row.Id}/remove",
            new RemoveCardRelationshipRequest(archived.ConcurrencyToken));
        remove.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Problem(remove)).GetProperty("code").GetString().ShouldBe("conflict");

        (await client.PostAsJsonAsync($"/api/cards/{blocked.Id}/unarchive",
            new UnarchiveCardRequest(archived.ConcurrencyToken, "back")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var rows = (await GetRelationshipsAsync(client, blocked.Id)).BlockedBy;
        rows.Count.ShouldBe(1);
        rows[0].Id.ShouldBe(row.Id);
    }

    [Test]
    public async Task Resolved_follows_the_blockers_Done_Canceled_archived_state_and_reopen()
    {
        var board = await SeedBoardAsync("c100 resolved", withCanceledColumn: true);
        var blocked = await CreateCardAsync(board.Id, "blocked");
        var blocker = await CreateCardAsync(board.Id, "blocker");
        using var client = _factory.CreateClient();
        await AddCreatedAsync(client, blocked.Id, "CARD-0002");
        var blockedRevisions = (await GetCardAsync(client, blocked.Id)).RevisionCount;
        var columns = await ColumnsAsync(board.Id);

        async Task<CardRelationshipDto> Row()
        {
            var rows = (await GetRelationshipsAsync(client, blocked.Id)).BlockedBy;
            rows.Count.ShouldBe(1);
            return rows[0];
        }

        async Task Move(CardStatus status)
        {
            var current = await GetCardAsync(client, blocker.Id);
            (await client.PatchAsJsonAsync($"/api/cards/{blocker.Id}",
                new MoveCardRequest(columns[status], current.ConcurrencyToken, "closing")))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        async Task Reopen()
        {
            var current = await GetCardAsync(client, blocker.Id);
            (await client.PostAsJsonAsync($"/api/cards/{blocker.Id}/reopen",
                new ReopenCardRequest(current.ConcurrencyToken, "not finished")))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await Row()).Resolved.ShouldBeFalse();
        await Move(CardStatus.Done);
        var afterDone = await Row();
        afterDone.Resolved.ShouldBeTrue();
        afterDone.RelatedStatus.ShouldBe(CardStatus.Done);
        await Reopen();
        (await Row()).Resolved.ShouldBeFalse();
        await Move(CardStatus.Canceled);
        var afterCancel = await Row();
        afterCancel.Resolved.ShouldBeTrue();
        await Reopen();
        (await Row()).Resolved.ShouldBeFalse();

        var live = await GetCardAsync(client, blocker.Id);
        (await client.PostAsJsonAsync($"/api/cards/{blocker.Id}/archive",
            new ArchiveCardRequest(live.ConcurrencyToken, "shelved")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterArchive = await Row();
        afterArchive.Resolved.ShouldBeTrue();
        afterArchive.RelatedArchivedAt.ShouldNotBeNull();
        var shelved = await GetCardAsync(client, blocker.Id);
        (await client.PostAsJsonAsync($"/api/cards/{blocker.Id}/unarchive",
            new UnarchiveCardRequest(shelved.ConcurrencyToken, "back")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Row()).Resolved.ShouldBeFalse();

        (await GetCardAsync(client, blocked.Id)).RevisionCount.ShouldBe(blockedRevisions);
    }

    [Test]
    public async Task Remove_writes_RelationshipRemoved_rotates_the_token_and_clears_both_views()
    {
        var board = await SeedBoardAsync("c100 remove");
        var blocked = await CreateCardAsync(board.Id, "blocked");
        var blocker = await CreateCardAsync(board.Id, "blocker");
        using var client = _factory.CreateClient();
        var row = await AddCreatedAsync(client, blocked.Id, "CARD-0002");

        var inverse = (await GetRelationshipsAsync(client, blocker.Id)).Blocks;
        inverse.Count.ShouldBe(1);
        inverse[0].Id.ShouldBe(row.Id);
        inverse[0].RelatedIdentifier.ShouldBe("CARD-0001");
        var before = await GetCardAsync(client, blocked.Id);

        var response = await client.PostAsJsonAsync(
            $"/api/cards/{blocked.Id}/relationships/{row.Id}/remove",
            new RemoveCardRelationshipRequest(before.ConcurrencyToken, "unblocked", "operator"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var raw = await response.Content.ReadFromJsonAsync<JsonElement>();
        raw.TryGetProperty("blockers", out _).ShouldBeFalse();
        raw.TryGetProperty("openBlockerCount", out _).ShouldBeFalse();
        var card = raw.Deserialize<CardDto>(Json)!;
        card.Id.ShouldBe(blocked.Id);
        card.ConcurrencyToken.ShouldNotBe(before.ConcurrencyToken);

        (await GetRelationshipsAsync(client, blocked.Id)).BlockedBy.ShouldBeEmpty();
        (await GetRelationshipsAsync(client, blocker.Id)).Blocks.ShouldBeEmpty();
        var revisions = (await client.GetFromJsonAsync<List<CardRevisionDto>>(
            $"/api/cards/{blocked.Id}/revisions", Json))!;
        revisions[0].Kind.ShouldBe(CardRevisionKind.RelationshipRemoved);
        revisions[0].RelatedCardId.ShouldBe(blocker.Id);
        revisions[0].RelatedCardIdentifier.ShouldBe("CARD-0002");
        revisions[0].Reason.ShouldBe("unblocked");
        revisions[0].EditedBy.ShouldBe("operator");
        (await GetCardAsync(client, blocker.Id)).RevisionCount.ShouldBe(0);
    }

    [Test]
    public async Task Card_and_list_views_carry_open_blocker_count_and_blockers_excluding_resolved()
    {
        var board = await SeedBoardAsync("c100 summary");
        var a = await CreateCardAsync(board.Id, "blocked");
        await CreateCardAsync(board.Id, "first blocker");
        var c = await CreateCardAsync(board.Id, "second blocker");
        var d = await CreateCardAsync(board.Id, "unrelated");
        using var client = _factory.CreateClient();
        await AddCreatedAsync(client, a.Id, "CARD-0002");
        await AddCreatedAsync(client, a.Id, "CARD-0003");

        var view = await GetCardAsync(client, a.Id);
        view.OpenBlockerCount.ShouldBe(2);
        view.Blockers.ShouldNotBeNull().OrderBy(b => b.Identifier).ShouldBe(
        [
            new CardBlockerSummaryDto("CARD-0002", CardStatus.Backlog, false),
            new CardBlockerSummaryDto("CARD-0003", CardStatus.Backlog, false),
        ]);
        var listed = await ListRowAsync(client, board.Id, a.Id);
        listed.OpenBlockerCount.ShouldBe(view.OpenBlockerCount);
        listed.Blockers.ShouldNotBeNull().OrderBy(b => b.Identifier).ShouldBe(view.Blockers!.OrderBy(b => b.Identifier));

        var columns = await ColumnsAsync(board.Id);
        var cNow = await GetCardAsync(client, c.Id);
        (await client.PatchAsJsonAsync($"/api/cards/{c.Id}",
            new MoveCardRequest(columns[CardStatus.Done], cNow.ConcurrencyToken, "done")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var afterDone = await GetCardAsync(client, a.Id);
        afterDone.OpenBlockerCount.ShouldBe(1);
        afterDone.Blockers.ShouldNotBeNull().OrderBy(b => b.Identifier).ShouldBe(
        [
            new CardBlockerSummaryDto("CARD-0002", CardStatus.Backlog, false),
            new CardBlockerSummaryDto("CARD-0003", CardStatus.Done, true),
        ]);
        var listedAfter = await ListRowAsync(client, board.Id, a.Id);
        listedAfter.OpenBlockerCount.ShouldBe(1);
        listedAfter.Blockers.ShouldNotBeNull().Count.ShouldBe(2);

        var unrelated = await client.GetFromJsonAsync<JsonElement>($"/api/cards/{d.Id}");
        unrelated.TryGetProperty("openBlockerCount", out _).ShouldBeFalse();
        unrelated.TryGetProperty("blockers", out _).ShouldBeFalse();
    }

    [Test]
    public async Task Cross_board_blocker_resolves_on_the_blocked_cards_board_by_default_and_honours_relatedBoardId()
    {
        var board1 = await SeedBoardAsync("c100 cross one");
        var board2 = await CreateBoardAsync("c100 cross two");
        var blockedDefault = await CreateCardAsync(board1.Id, "blocked by default");
        var board1Card = await CreateCardAsync(board1.Id, "board one blocker");
        var blockedFenced = await CreateCardAsync(board1.Id, "blocked across boards");
        await CreateCardAsync(board2.Id, "board two first");
        var board2Card = await CreateCardAsync(board2.Id, "board two blocker");
        board1Card.Identifier.ShouldBe("CARD-0002");
        board2Card.Identifier.ShouldBe("CARD-0002");
        using var client = _factory.CreateClient();

        var unfenced = await AddCreatedAsync(client, blockedDefault.Id, "CARD-0002");
        unfenced.RelatedCardId.ShouldBe(board1Card.Id);

        var current = await GetCardAsync(client, blockedFenced.Id);
        var response = await PostAddAsync(client, blockedFenced.Id, new
        {
            concurrencyToken = current.ConcurrencyToken,
            relatedCard = "CARD-0002",
            kind = "BlockedBy",
            relatedBoardId = board2.Id,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var fenced = (await response.Content.ReadFromJsonAsync<CardRelationshipDto>(Json))!;
        fenced.RelatedCardId.ShouldBe(board2Card.Id);
        fenced.RelatedBoardId.ShouldBe(board2.Id);
    }

    private async Task<BoardDetailDto> SeedBoardAsync(string name, bool withCanceledColumn = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"C100 Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = Path.Combine(Path.GetTempPath(), $"antiphon-c100-{Guid.NewGuid():N}"),
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        _projectId = project.Id;

        var board = await CreateBoardAsync(name);
        if (withCanceledColumn)
        {
            db.BoardColumns.Add(new BoardColumn
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                StateKey = "canceled",
                Name = "Canceled",
                ColumnOrder = 5,
                CardStatus = CardStatus.Canceled,
                IsTerminal = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        return board;
    }

    private async Task<BoardDetailDto> CreateBoardAsync(string name)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BoardService>().CreateAsync(
            new CreateBoardRequest(_projectId, name), CancellationToken.None);
    }

    private async Task<CardDto> CreateCardAsync(Guid boardId, string title)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CardService>().CreateAsync(
            boardId, new CreateCardRequest(null, title, string.Empty), CancellationToken.None);
    }

    private async Task<Dictionary<CardStatus, Guid>> ColumnsAsync(Guid boardId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.BoardColumns.AsNoTracking().Where(c => c.BoardId == boardId)
            .ToDictionaryAsync(c => c.CardStatus, c => c.Id);
    }

    private static async Task<CardDto> GetCardAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<CardDto>($"/api/cards/{id}", Json))!;

    private static async Task<CardDto> ListRowAsync(HttpClient client, Guid boardId, Guid cardId)
    {
        var page = (await client.GetFromJsonAsync<CardListDto>($"/api/cards?boardId={boardId}", Json))!;
        return page.Cards.Single(c => c.Id == cardId);
    }

    private static async Task<CardRelationshipsDto> GetRelationshipsAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<CardRelationshipsDto>($"/api/cards/{id}/relationships", Json))!;

    private static Task<HttpResponseMessage> PostAddAsync(HttpClient client, Guid id, object body) =>
        client.PostAsJsonAsync($"/api/cards/{id}/relationships", body);

    /// <summary>POSTs a BlockedBy row with the card's current token.</summary>
    private static async Task<HttpResponseMessage> AddAsync(HttpClient client, Guid id, string relatedCard)
    {
        var current = await GetCardAsync(client, id);
        return await PostAddAsync(client, id,
            new { concurrencyToken = current.ConcurrencyToken, relatedCard, kind = "BlockedBy" });
    }

    private static async Task<CardRelationshipDto> AddCreatedAsync(HttpClient client, Guid id, string relatedCard)
    {
        var response = await AddAsync(client, id, relatedCard);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CardRelationshipDto>(Json))!;
    }

    private static Task<JsonElement> Problem(HttpResponseMessage response) =>
        response.Content.ReadFromJsonAsync<JsonElement>();
}
