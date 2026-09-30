using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[NotInParallel]
[ClassDataSource<CardReadApiFixture>(Shared = SharedType.PerTestSession)]
[Category("Integration")]
public class CardSearchApiTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() } };
    private readonly CardReadApiFixture _fixture;
    public CardSearchApiTests(CardReadApiFixture fixture) => _fixture = fixture;
    [Before(Test)] public Task ResetAsync() => _fixture.ResetAsync();
    [After(Test)] public Task CleanupAsync() => _fixture.CleanupAsync();

    private async Task<CardSearchDto> SearchAsync(string q, Guid? board = null, int? limit = null,
        string? token = null, bool all = false, CardStatus? status = null)
    {
        using var client = _fixture.CreateClient();
        var path = $"/api/cards/search?q={Uri.EscapeDataString(q)}&includeArchived={all.ToString().ToLowerInvariant()}";
        if (board is not null) path += $"&boardId={board}";
        if (status is not null) path += $"&status={status}";
        if (limit is not null) path += $"&limit={limit}";
        if (token is not null) path += $"&pageToken={Uri.EscapeDataString(token)}";
        return (await client.GetFromJsonAsync<CardSearchDto>(path, Json))!;
    }

    private async Task Assert422Async(string path, string field)
    {
        using var client = _fixture.CreateClient();
        var response = await client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().ShouldBe("validation_failed");
        json.GetProperty("errors").GetProperty(field).GetArrayLength().ShouldBeGreaterThan(0);
    }

    private async Task AssertChangedAsync(string q, Guid board, string token)
    {
        using var client = _fixture.CreateClient();
        var response = await client.GetAsync($"/api/cards/search?q={Uri.EscapeDataString(q)}&boardId={board}&limit=2&pageToken={Uri.EscapeDataString(token)}");
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().ShouldBe("card_page_changed");
    }

    [Test] public async Task Search_finds_an_old_card_beyond_the_first_500()
    {
        var board = await _fixture.BoardAsync();
        var cards = await _fixture.CardsAsync(board.Id, 605);
        await using (var db = _fixture.Writer())
            await db.Cards.Where(c => c.Id == cards[^1].Id).ExecuteUpdateAsync(s =>
                s.SetProperty(c => c.Description, "needle-0846"));
        var result = await SearchAsync("needle-0846", board.Id);
        result.Total.ShouldBe(1);
        result.Cards.Single().Id.ShouldBe(cards[^1].Id);
    }

    [Test] public async Task Every_public_search_field_matches_independently()
    {
        var board = await _fixture.BoardAsync();
        var cards = await _fixture.CardsAsync(board.Id, 6);
        await using (var db = _fixture.Writer())
        {
            var ids = cards.Select(c => c.Id).ToArray();
            await db.Cards.Where(c => c.Id == ids[0]).ExecuteUpdateAsync(s => s.SetProperty(c => c.Identifier, "MARKIDENT-1"));
            await db.Cards.Where(c => c.Id == ids[1]).ExecuteUpdateAsync(s => s.SetProperty(c => c.Alias, "markalias"));
            await db.Cards.Where(c => c.Id == ids[2]).ExecuteUpdateAsync(s => s.SetProperty(c => c.Title, "marktitle"));
            await db.Cards.Where(c => c.Id == ids[3]).ExecuteUpdateAsync(s => s.SetProperty(c => c.Description, "markdescription"));
            await db.Cards.Where(c => c.Id == ids[4]).ExecuteUpdateAsync(s => s.SetProperty(c => c.LabelsJson, "[\"marklabel\"]"));
            await db.Cards.Where(c => c.Id == ids[5]).ExecuteUpdateAsync(s => s.SetProperty(c => c.TerminalReason, "markreason"));
        }
        foreach (var (term, card) in new[] { "MARKIDENT", "MARKALIAS", "MARKTITLE", "MARKDESCRIPTION", "MARKLABEL", "MARKREASON" }
            .Zip(cards))
        {
            var result = await SearchAsync(term, board.Id);
            result.Total.ShouldBe(1, term);
            result.Cards.Single().Id.ShouldBe(card.Id, term);
        }
    }

    [Test] public async Task Description_and_terminal_body_matches_precede_previewing()
    {
        var board = await _fixture.BoardAsync();
        var cards = await _fixture.CardsAsync(board.Id, 2);
        var body = string.Concat(Enumerable.Repeat("abcd ", 60)) + "late-needle";
        await using (var db = _fixture.Writer())
        {
            await db.Cards.Where(c => c.Id == cards[0].Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.Description, body));
            await db.Cards.Where(c => c.Id == cards[1].Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.TerminalReason, body));
        }
        var result = await SearchAsync("late-needle", board.Id);
        result.Total.ShouldBe(2);
        result.Cards.All(c => c.HasMore).ShouldBeTrue();
        result.Cards.All(c => !c.Description.Contains("late-needle") &&
            !(c.TerminalReason?.Contains("late-needle") ?? false)).ShouldBeTrue();
    }

    [Test] public async Task Totals_and_search_pages_cover_all_matches()
    {
        var board = await _fixture.BoardAsync();
        await _fixture.CardsAsync(board.Id, 605, "common-needle");
        var expected = await _fixture.OracleAsync(board.Id);
        var actual = new List<Guid>();
        string? token = null;
        for (var i = 0; i < 5; i++)
        {
            var page = await SearchAsync("common-needle", board.Id, token: token);
            page.Total.ShouldBe(605);
            actual.AddRange(page.Cards.Select(c => c.Id));
            if (!page.Truncated)
            {
                page.NextPageToken.ShouldBeNull();
                break;
            }
            token = page.NextPageToken;
            token.ShouldNotBeNullOrWhiteSpace();
        }
        actual.ShouldBe(expected);
        var empty = await SearchAsync("no-such-needle", board.Id);
        empty.Total.ShouldBe(0);
        empty.Cards.Count.ShouldBe(0);
        empty.Truncated.ShouldBeFalse();
        empty.NextPageToken.ShouldBeNull();
    }

    [Test] public async Task Search_filters_include_closed_and_optional_archived_cards()
    {
        var board = await _fixture.BoardAsync();
        var cards = await _fixture.CardsAsync(board.Id, 3, "state-needle");
        await using (var db = _fixture.Writer())
        {
            await db.Cards.Where(c => c.Id == cards[0].Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CardStatus.Done));
            await db.Cards.Where(c => c.Id == cards[1].Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CardStatus.Canceled));
            await db.Cards.Where(c => c.Id == cards[2].Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.ArchivedAt, DateTime.UtcNow));
        }
        (await SearchAsync("state-needle", board.Id)).Total.ShouldBe(2);
        (await SearchAsync("state-needle", board.Id, all: true)).Total.ShouldBe(3);
        (await SearchAsync("state-needle", board.Id, status: CardStatus.Done)).Cards.Single().Id.ShouldBe(cards[0].Id);
    }

    [Test] public async Task Literal_patterns_and_decoded_labels_do_not_overmatch()
    {
        var board = await _fixture.BoardAsync();
        var cards = await _fixture.CardsAsync(board.Id, 4);
        await using (var db = _fixture.Writer())
        {
            await db.Cards.Where(c => c.Id == cards[0].Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.LabelsJson, "[\"rate%done\"]"));
            await db.Cards.Where(c => c.Id == cards[1].Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.LabelsJson, "[\"part_name\"]"));
            await db.Cards.Where(c => c.Id == cards[2].Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.LabelsJson, "[\"snow 雪\"]"));
            await db.Cards.Where(c => c.Id == cards[3].Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.Title, "rateXdone partXname"));
        }
        (await SearchAsync("rate%done", board.Id)).Cards.Single().Id.ShouldBe(cards[0].Id);
        (await SearchAsync("part_name", board.Id)).Cards.Single().Id.ShouldBe(cards[1].Id);
        (await SearchAsync("雪", board.Id)).Cards.Single().Id.ShouldBe(cards[2].Id);
        (await SearchAsync("rateXdone", board.Id)).Cards.Single().Id.ShouldBe(cards[3].Id);
    }

    [Test] public async Task Invalid_search_and_changed_search_tokens_are_rejected()
    {
        var board = await _fixture.BoardAsync();
        await _fixture.CardsAsync(board.Id, 3, "search-needle");
        await Assert422Async("/api/cards/search", "q");
        await Assert422Async("/api/cards/search?q=%20%20", "q");
        await Assert422Async("/api/cards/search?q=" + new string('a', 501), "q");
        var token = (await SearchAsync("search-needle", board.Id, 1)).NextPageToken!;
        await Assert422Async($"/api/cards/search?q=other&boardId={board.Id}&limit=1&pageToken={Uri.EscapeDataString(token)}", "pageToken");
        (await SearchAsync(" search-needle ", board.Id, 1, token)).Cards.Count.ShouldBe(1);
    }

    [Test] public async Task Private_notes_comments_and_revisions_are_not_search_sources()
    {
        var board = await _fixture.BoardAsync();
        var cards = await _fixture.CardsAsync(board.Id, 1, "public-needle");
        await using (var db = _fixture.Writer())
            await db.Cards.Where(c => c.Id == cards[0].Id).ExecuteUpdateAsync(s =>
                s.SetProperty(c => c.PrivateNotes, "secret-needle").SetProperty(c => c.ArchivedReason, "archive-needle"));
        (await SearchAsync("secret-needle", board.Id)).Total.ShouldBe(0);
        (await SearchAsync("archive-needle", board.Id, all: true)).Total.ShouldBe(0);
        using var client = _fixture.CreateClient();
        var response = await client.GetStringAsync($"/api/cards/search?q=public-needle&boardId={board.Id}");
        response.ShouldNotContain("secret-needle");
        response.ShouldNotContain("privateNotes");
    }

    [Test] public async Task Search_membership_change_invalidates_the_next_page()
    {
        var board = await _fixture.BoardAsync();
        var cards = await _fixture.CardsAsync(board.Id, 4, "membership-needle");
        var token = (await SearchAsync("membership-needle", board.Id, 2)).NextPageToken!;
        await using (var db = _fixture.Writer())
            await db.Cards.Where(c => c.Id == cards[3].Id).ExecuteUpdateAsync(s =>
                s.SetProperty(c => c.Title, "departed").SetProperty(c => c.Description, "departed"));
        await AssertChangedAsync("membership-needle", board.Id, token);
        (await SearchAsync("membership-needle", board.Id)).Total.ShouldBe(3);
    }

    [Test] public async Task Nondefault_page_limit_and_null_fields_work()
    {
        var board = await _fixture.BoardAsync();
        await _fixture.CardsAsync(board.Id, 5, "null-fields-needle");
        var expected = await _fixture.OracleAsync(board.Id);
        var actual = new List<Guid>();
        string? token = null;
        for (var i = 0; i < 5; i++)
        {
            var page = await SearchAsync("null-fields-needle", board.Id, 2, token);
            page.Total.ShouldBe(5);
            actual.AddRange(page.Cards.Select(c => c.Id));
            if (!page.Truncated) break;
            token = page.NextPageToken;
        }
        actual.ShouldBe(expected);
    }
}
