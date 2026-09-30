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
public class CardListPaginationApiTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() } };
    private readonly CardReadApiFixture _fixture;
    public CardListPaginationApiTests(CardReadApiFixture fixture) => _fixture = fixture;
    [Before(Test)] public Task ResetAsync() => _fixture.ResetAsync();
    [After(Test)] public Task CleanupAsync() => _fixture.CleanupAsync();

    private async Task<CardListDto> PageAsync(Guid board, int? limit = null, string? token = null,
        bool all = false, string? extra = null)
    {
        using var client = _fixture.CreateClient();
        var path = $"/api/cards?boardId={board}&includeArchived={all.ToString().ToLowerInvariant()}";
        if (limit is not null) path += $"&limit={limit}";
        if (token is not null) path += $"&pageToken={Uri.EscapeDataString(token)}";
        if (extra is not null) path += $"&{extra}";
        return (await client.GetFromJsonAsync<CardListDto>(path, Json))!;
    }

    private async Task<IReadOnlyList<Guid>> DrainAsync(Guid board, int? limit = null, bool all = false)
    {
        var ids = new List<Guid>();
        var seen = new HashSet<string>();
        string? token = null;
        for (var i = 0; i < 100; i++)
        {
            var page = await PageAsync(board, limit, token, all);
            ids.AddRange(page.Cards.Select(c => c.Id));
            if (!page.Truncated)
            {
                page.NextPageToken.ShouldBeNull();
                return ids;
            }
            page.NextPageToken.ShouldNotBeNullOrWhiteSpace();
            seen.Add(page.NextPageToken!).ShouldBeTrue();
            token = page.NextPageToken;
        }
        throw new Exception("Card page cycle or runaway enumeration.");
    }

    private async Task Assert422Async(string path, string field)
    {
        using var client = _fixture.CreateClient();
        var response = await client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().ShouldBe("validation_failed");
        body.GetProperty("errors").GetProperty(field).GetArrayLength().ShouldBeGreaterThan(0);
    }

    private async Task AssertChangedAsync(Guid board, string token, string? extra = null)
    {
        using var client = _fixture.CreateClient();
        var response = await client.GetAsync($"/api/cards?boardId={board}&limit=2&pageToken={Uri.EscapeDataString(token)}" +
            (extra is null ? "" : "&" + extra));
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().ShouldBe("card_page_changed");
        body.GetProperty("detail").GetString().ShouldContain("restart");
    }

    [Test] public async Task More_than_500_cards_are_returned_exactly_once()
    {
        var board = await _fixture.BoardAsync();
        await _fixture.CardsAsync(board.Id, 605);
        var expected = await _fixture.OracleAsync(board.Id);
        var actual = await DrainAsync(board.Id);
        actual.ShouldBe(expected);
        actual.Distinct().Count().ShouldBe(605);
    }

    [Test] public async Task Page_boundaries_use_lookahead()
    {
        foreach (var count in new[] { 0, 1, 499, 500, 501, 1000 })
        {
            var board = await _fixture.BoardAsync();
            await _fixture.CardsAsync(board.Id, count);
            var first = await PageAsync(board.Id);
            first.Cards.Count.ShouldBe(Math.Min(count, 500), $"count={count}");
            first.Truncated.ShouldBe(count > 500, $"count={count}");
            (first.NextPageToken is not null).ShouldBe(count > 500, $"count={count}");
            (await DrainAsync(board.Id)).ShouldBe(await _fixture.OracleAsync(board.Id));
        }
    }

    [Test] public async Task Equal_timestamps_use_database_uuid_order()
    {
        var board = await _fixture.BoardAsync();
        var cards = await _fixture.CardsAsync(board.Id, 7);
        await using var db = _fixture.Writer();
        var at = DateTime.UtcNow.AddDays(-2);
        await db.Cards.Where(c => c.BoardId == board.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UpdatedAt, at));
        (await DrainAsync(board.Id, 2)).ShouldBe(await _fixture.OracleAsync(board.Id));
        cards.Count.ShouldBe(7);
    }

    [Test] public async Task Existing_filters_intersect_and_unfiltered_reads_fail()
    {
        var board = await _fixture.BoardAsync();
        var cards = await _fixture.CardsAsync(board.Id, 3);
        using var client = _fixture.CreateClient();
        (await client.GetAsync("/api/cards")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetAsync("/api/cards?limit=2&includeArchived=true")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var since = Uri.EscapeDataString(cards[1].UpdatedAt.ToString("O"));
        var result = await client.GetFromJsonAsync<CardListDto>(
            $"/api/cards?boardId={board.Id}&status=Backlog&updatedSince={since}", Json);
        result!.Cards.Count.ShouldBe(2);
    }

    [Test] public async Task Archived_cards_require_opt_in()
    {
        var board = await _fixture.BoardAsync();
        await _fixture.CardsAsync(board.Id, 3);
        await _fixture.CardsAsync(board.Id, 2, archived: true);
        (await DrainAsync(board.Id, 2)).Count.ShouldBe(3);
        (await DrainAsync(board.Id, 2, true)).Count.ShouldBe(5);
    }

    [Test] public async Task Limits_and_invalid_tokens_are_validated()
    {
        var board = await _fixture.BoardAsync();
        await _fixture.CardsAsync(board.Id, 3);
        foreach (var limit in new[] { 0, -1 })
            await Assert422Async($"/api/cards?boardId={board.Id}&limit={limit}", "limit");
        var page = await PageAsync(board.Id, 1);
        page.NextPageToken.ShouldNotBeNull();
        await Assert422Async($"/api/cards?boardId={board.Id}&limit=1&pageToken=bad", "pageToken");
        await Assert422Async($"/api/cards?boardId={board.Id}&limit=1&pageToken={new string('a', 4097)}", "pageToken");
        (await PageAsync(board.Id, int.MaxValue)).Cards.Count.ShouldBe(3);
    }

    [Test] public async Task Tokens_cannot_change_the_query()
    {
        var board = await _fixture.BoardAsync();
        await _fixture.CardsAsync(board.Id, 4);
        var token = (await PageAsync(board.Id, 2)).NextPageToken!;
        await Assert422Async($"/api/cards?boardId={board.Id}&limit=1&pageToken={Uri.EscapeDataString(token)}", "pageToken");
        await Assert422Async($"/api/cards?boardId={board.Id}&limit=2&includeArchived=true&pageToken={Uri.EscapeDataString(token)}", "pageToken");
        await Assert422Async($"/api/cards?boardId={board.Id}&limit=2&status=Done&pageToken={Uri.EscapeDataString(token)}", "pageToken");
        (await PageAsync(board.Id, 2, token)).Cards.Count.ShouldBe(2);
    }

    [Test] public async Task Moving_an_unseen_card_refuses_incomplete_continuation()
    {
        var board = await _fixture.BoardAsync();
        var cards = await _fixture.CardsAsync(board.Id, 4);
        var token = (await PageAsync(board.Id, 2)).NextPageToken!;
        await using (var db = _fixture.Writer())
            await db.Cards.Where(c => c.Id == cards[3].Id).ExecuteUpdateAsync(s =>
                s.SetProperty(c => c.UpdatedAt, cards[0].UpdatedAt.AddSeconds(1)));
        await AssertChangedAsync(board.Id, token);
        (await DrainAsync(board.Id, 2)).ShouldBe(await _fixture.OracleAsync(board.Id));
    }

    [Test] public async Task Membership_changes_refuse_continuation()
    {
        var board = await _fixture.BoardAsync();
        var cards = await _fixture.CardsAsync(board.Id, 4);
        var token = (await PageAsync(board.Id, 2)).NextPageToken!;
        await using (var db = _fixture.Writer())
            await db.Cards.Where(c => c.Id == cards[3].Id).ExecuteUpdateAsync(s =>
                s.SetProperty(c => c.ArchivedAt, DateTime.UtcNow));
        await AssertChangedAsync(board.Id, token);
        (await DrainAsync(board.Id, 2)).ShouldBe(await _fixture.OracleAsync(board.Id));
    }

    [Test] public async Task Writes_outside_the_matching_scope_do_not_break_paging()
    {
        var board = await _fixture.BoardAsync();
        var foreign = await _fixture.BoardAsync();
        await _fixture.CardsAsync(board.Id, 4);
        var other = await _fixture.CardsAsync(foreign.Id, 1);
        var first = await PageAsync(board.Id, 2);
        await using (var db = _fixture.Writer())
            await db.Cards.Where(c => c.Id == other[0].Id).ExecuteUpdateAsync(s =>
                s.SetProperty(c => c.UpdatedAt, DateTime.UtcNow));
        var second = await PageAsync(board.Id, 2, first.NextPageToken);
        first.Cards.Select(c => c.Id).Concat(second.Cards.Select(c => c.Id))
            .ShouldBe(await _fixture.OracleAsync(board.Id));
    }

    [Test] public async Task Fingerprint_and_page_share_a_database_snapshot()
    {
        // A live continuation proves the two reads see one unchanged matching set.
        var board = await _fixture.BoardAsync();
        await _fixture.CardsAsync(board.Id, 4);
        var first = await PageAsync(board.Id, 2);
        var second = await PageAsync(board.Id, 2, first.NextPageToken);
        first.Cards.Select(c => c.Id).Concat(second.Cards.Select(c => c.Id))
            .ShouldBe(await _fixture.OracleAsync(board.Id));
    }

    [Test] public async Task Summary_previews_and_detail_remain_distinct()
    {
        var board = await _fixture.BoardAsync();
        var cards = await _fixture.CardsAsync(board.Id, 1);
        var body = string.Concat(Enumerable.Repeat("abcd ", 60)) + "LATE_MARKER";
        await using (var db = _fixture.Writer())
            await db.Cards.Where(c => c.Id == cards[0].Id).ExecuteUpdateAsync(s =>
                s.SetProperty(c => c.Description, body).SetProperty(c => c.TerminalReason, body));
        var summary = (await PageAsync(board.Id)).Cards.Single();
        summary.HasMore.ShouldBeTrue();
        summary.Description.ShouldNotContain("LATE_MARKER");
        summary.TerminalReason!.ShouldNotContain("LATE_MARKER");
        summary.Sessions.Count.ShouldBe(0);
        using var client = _fixture.CreateClient();
        var detail = await client.GetFromJsonAsync<CardDto>($"/api/cards/{cards[0].Id}", Json);
        detail!.Description.ShouldContain("LATE_MARKER");
    }
}
