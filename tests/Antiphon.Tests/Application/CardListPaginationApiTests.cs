using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Antiphon.Server.Application.Settings;
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
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var actual = await DrainAsync(board.Id);
        clock.Stop();
        Console.WriteLine($"CARD-0846 list 605 elapsedMs={clock.ElapsedMilliseconds}");
        await _fixture.DiagnosePlansAsync(board.Id);
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
        var firstId = cards[0].Id;
        var secondId = cards[1].Id;
        await db.Cards.Where(c => c.Id == firstId).ExecuteUpdateAsync(s =>
            s.SetProperty(c => c.UpdatedAt, at.AddTicks(10)));
        await db.Cards.Where(c => c.Id == secondId).ExecuteUpdateAsync(s =>
            s.SetProperty(c => c.UpdatedAt, at.AddTicks(20)));
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
        foreach (var malformed in new[] { "boardId=not-a-guid", "status=NoSuchStatus",
            "updatedSince=not-a-date", $"boardId={board.Id}&includeArchived=maybe" })
            (await client.GetAsync("/api/cards?" + malformed)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
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
        await using (var db = _fixture.Writer())
        {
            var projectId = await db.Boards.Where(b => b.Id == board.Id).Select(b => b.ProjectId).SingleAsync();
            await db.Boards.Where(b => b.Id == board.Id).ExecuteUpdateAsync(s =>
                s.SetProperty(b => b.ArchivedAt, DateTime.UtcNow));
            await db.Projects.Where(p => p.Id == projectId).ExecuteUpdateAsync(s =>
                s.SetProperty(p => p.ArchivedAt, DateTime.UtcNow));
        }
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
        var raw = page.NextPageToken!.Replace('-', '+').Replace('_', '/');
        var issued = JsonNode.Parse(System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(raw.PadRight((raw.Length + 3) / 4 * 4, '='))))!.AsObject();
        static string Encode(JsonObject json) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        foreach (var mutate in new Action<JsonObject>[]
        {
            j => j["Version"] = 2,
            j => j.Remove("Fingerprint"),
            j => j["AfterUpdatedAt"] = "2026-09-30T00:00:00",
            j => j["AfterId"] = Guid.Empty.ToString(),
            j => j["Fingerprint"] = "bad"
        })
        {
            var altered = (JsonObject)issued.DeepClone();
            mutate(altered);
            await Assert422Async($"/api/cards?boardId={board.Id}&limit=1&pageToken={Uri.EscapeDataString(Encode(altered))}", "pageToken");
        }
        var oversized = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
            issued.ToJsonString() + new string(' ', 4096))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await Assert422Async($"/api/cards?boardId={board.Id}&limit=1&pageToken={Uri.EscapeDataString(oversized)}", "pageToken");
        using var client = _fixture.CreateClient();
        (await client.GetAsync($"/api/cards?boardId={board.Id}&limit=oops")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PageAsync(board.Id, int.MaxValue)).Cards.Count.ShouldBe(3);
        (await PageAsync(board.Id, 501)).Cards.Count.ShouldBe(3);
        using var scope = _fixture.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IOptions<CardsSettings>>().Value;
        var original = settings.MaxListResults;
        try
        {
            foreach (var ceiling in new[] { 0, -1 })
            {
                settings.MaxListResults = ceiling;
                (await PageAsync(board.Id)).Cards.Count.ShouldBe(1, $"ceiling={ceiling}");
            }
        }
        finally { settings.MaxListResults = original; }
    }

    [Test] public async Task Tokens_cannot_change_the_query()
    {
        var board = await _fixture.BoardAsync();
        await _fixture.CardsAsync(board.Id, 4);
        var token = (await PageAsync(board.Id, 2)).NextPageToken!;
        await Assert422Async($"/api/cards?boardId={board.Id}&limit=1&pageToken={Uri.EscapeDataString(token)}", "pageToken");
        await Assert422Async($"/api/cards?boardId={Guid.NewGuid()}&limit=2&pageToken={Uri.EscapeDataString(token)}", "pageToken");
        await Assert422Async($"/api/cards?boardId={board.Id}&limit=2&includeArchived=true&pageToken={Uri.EscapeDataString(token)}", "pageToken");
        await Assert422Async($"/api/cards?boardId={board.Id}&limit=2&status=Done&pageToken={Uri.EscapeDataString(token)}", "pageToken");
        await Assert422Async($"/api/cards?boardId={board.Id}&limit=2&updatedSince={Uri.EscapeDataString(DateTime.UtcNow.AddDays(-10).ToString("O"))}&pageToken={Uri.EscapeDataString(token)}", "pageToken");
        (await PageAsync(board.Id, 2, token)).Cards.Count.ShouldBe(2);
        var equivalentBoard = await _fixture.BoardAsync();
        await _fixture.CardsAsync(equivalentBoard.Id, 501);
        var defaultToken = (await PageAsync(equivalentBoard.Id)).NextPageToken!;
        (await PageAsync(equivalentBoard.Id, 501, defaultToken)).Cards.Count.ShouldBe(1);
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

        var secondBoard = await _fixture.BoardAsync();
        var secondCards = await _fixture.CardsAsync(secondBoard.Id, 4);
        var secondToken = (await PageAsync(secondBoard.Id, 2)).NextPageToken!;
        var between = secondCards[0].UpdatedAt.AddMilliseconds(-500);
        await using (var db = _fixture.Writer())
            await db.Cards.Where(c => c.Id == secondCards[3].Id).ExecuteUpdateAsync(s =>
                s.SetProperty(c => c.UpdatedAt, between));
        await AssertChangedAsync(secondBoard.Id, secondToken);
        (await DrainAsync(secondBoard.Id, 2)).ShouldBe(await _fixture.OracleAsync(secondBoard.Id));
    }

    [Test] public async Task Membership_changes_refuse_continuation()
    {
        foreach (var change in new[] { "archive", "insert", "delete-returned", "delete-unseen",
            "status-depart", "status-arrive", "unarchive" })
        {
            var board = await _fixture.BoardAsync();
            var cards = await _fixture.CardsAsync(board.Id, 6);
            var targetId = cards[5].Id;
            if (change == "status-arrive")
                await using (var setup = _fixture.Writer())
                    await setup.Cards.Where(c => c.Id == targetId).ExecuteUpdateAsync(s =>
                        s.SetProperty(c => c.Status, CardStatus.Done));
            if (change == "unarchive")
                await using (var setup = _fixture.Writer())
                    await setup.Cards.Where(c => c.Id == targetId).ExecuteUpdateAsync(s =>
                        s.SetProperty(c => c.ArchivedAt, DateTime.UtcNow));
            var statusFilter = change.StartsWith("status-") ? "status=Backlog" : null;
            var token = (await PageAsync(board.Id, 2, extra: statusFilter)).NextPageToken!;
            await using (var db = _fixture.Writer())
            {
                switch (change)
                {
                    case "archive":
                        await db.Cards.Where(c => c.Id == targetId).ExecuteUpdateAsync(s =>
                            s.SetProperty(c => c.ArchivedAt, DateTime.UtcNow));
                        break;
                    case "insert":
                        await _fixture.CardsAsync(board.Id, 1);
                        break;
                    case "delete-returned":
                        var returnedId = cards[0].Id;
                        await db.Cards.Where(c => c.Id == returnedId).ExecuteDeleteAsync();
                        break;
                    case "delete-unseen":
                        await db.Cards.Where(c => c.Id == targetId).ExecuteDeleteAsync();
                        break;
                    case "status-depart":
                        await db.Cards.Where(c => c.Id == targetId).ExecuteUpdateAsync(s =>
                            s.SetProperty(c => c.Status, CardStatus.Done));
                        break;
                    case "status-arrive":
                        await db.Cards.Where(c => c.Id == targetId).ExecuteUpdateAsync(s =>
                            s.SetProperty(c => c.Status, CardStatus.Backlog));
                        break;
                    case "unarchive":
                        await db.Cards.Where(c => c.Id == targetId).ExecuteUpdateAsync(s =>
                            s.SetProperty(c => c.ArchivedAt, (DateTime?)null));
                        break;
                }
            }
            await AssertChangedAsync(board.Id, token, statusFilter);
        }

        var replacementBoard = await _fixture.BoardAsync();
        await _fixture.CardsAsync(replacementBoard.Id, 4);
        var tie = DateTime.UtcNow.AddDays(-3);
        await using (var db = _fixture.Writer())
            await db.Cards.Where(c => c.BoardId == replacementBoard.Id).ExecuteUpdateAsync(s =>
                s.SetProperty(c => c.UpdatedAt, tie));
        var ordered = await _fixture.OracleAsync(replacementBoard.Id);
        var replacementToken = (await PageAsync(replacementBoard.Id, 2)).NextPageToken!;
        var removed = ordered[^1];
        await using (var db = _fixture.Writer())
            await db.Cards.Where(c => c.Id == removed).ExecuteDeleteAsync();
        var added = await _fixture.CardsAsync(replacementBoard.Id, 1);
        await using (var db = _fixture.Writer())
        {
            var addedId = added[0].Id;
            await db.Cards.Where(c => c.Id == addedId).ExecuteUpdateAsync(s =>
                s.SetProperty(c => c.UpdatedAt, tie));
        }
        (await _fixture.OracleAsync(replacementBoard.Id)).Count.ShouldBe(4);
        await AssertChangedAsync(replacementBoard.Id, replacementToken);
    }

    [Test] public async Task Writes_outside_the_matching_scope_do_not_break_paging()
    {
        var board = await _fixture.BoardAsync();
        var foreign = await _fixture.BoardAsync();
        await _fixture.CardsAsync(board.Id, 4);
        var other = await _fixture.CardsAsync(foreign.Id, 1);
        var archived = await _fixture.CardsAsync(board.Id, 1, archived: true);
        var first = await PageAsync(board.Id, 2);
        await using (var db = _fixture.Writer())
            await db.Cards.Where(c => c.Id == other[0].Id).ExecuteUpdateAsync(s =>
                s.SetProperty(c => c.UpdatedAt, DateTime.UtcNow));
        await using (var db = _fixture.Writer())
            await db.Cards.Where(c => c.Id == archived[0].Id).ExecuteUpdateAsync(s =>
                s.SetProperty(c => c.UpdatedAt, DateTime.UtcNow));
        var second = await PageAsync(board.Id, 2, first.NextPageToken);
        first.Cards.Select(c => c.Id).Concat(second.Cards.Select(c => c.Id))
            .ShouldBe(await _fixture.OracleAsync(board.Id));
    }

    [Test] public async Task Fingerprint_and_page_share_a_database_snapshot()
    {
        var board = await _fixture.BoardAsync();
        var cards = await _fixture.CardsAsync(board.Id, 4);
        var before = await _fixture.OracleAsync(board.Id);
        _fixture.Probe.Arm(board.Id);
        var pending = PageAsync(board.Id, 2);
        try
        {
            await _fixture.Probe.WaitForPageAsync();
            await using (var db = _fixture.Writer())
                (await db.Cards.Where(c => c.Id == cards[2].Id).ExecuteUpdateAsync(s =>
                    s.SetProperty(c => c.UpdatedAt, cards[0].UpdatedAt.AddSeconds(1)))).ShouldBe(1);
            (await _fixture.OracleAsync(board.Id))[0].ShouldBe(cards[2].Id);
        }
        finally { _fixture.Probe.Release(); }
        var first = await pending;
        _fixture.Probe.GateHits.ShouldBe(1);
        _fixture.Probe.MetadataClosedAtGate.ShouldBeTrue();
        _fixture.Probe.SameTransaction.ShouldBeTrue();
        first.Cards.Select(c => c.Id).ShouldBe(before.Take(2));
        first.Truncated.ShouldBeTrue();
        await AssertChangedAsync(board.Id, first.NextPageToken!);
        _fixture.Probe.Disarm();
        (await DrainAsync(board.Id, 2)).ShouldBe(await _fixture.OracleAsync(board.Id));
    }

    [Test] public async Task Summary_previews_and_detail_remain_distinct()
    {
        var board = await _fixture.BoardAsync();
        var cards = await _fixture.CardsAsync(board.Id, 1);
        var body = string.Concat(Enumerable.Repeat("abcd ", 60)) + "LATE_MARKER";
        var sessionId = Guid.NewGuid();
        await using (var db = _fixture.Writer())
        {
            await db.Cards.Where(c => c.Id == cards[0].Id).ExecuteUpdateAsync(s =>
                s.SetProperty(c => c.Description, body).SetProperty(c => c.TerminalReason, body)
                 .SetProperty(c => c.PrivateNotes, "private-marker-0846"));
            var now = DateTime.UtcNow;
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId, CardId = cards[0].Id, DefinitionName = "synthetic-card-reader",
                AgentKind = AgentKind.Raw, Status = SessionStatus.Stopped, Cwd = "/tmp/card-read",
                Cols = 120, Rows = 30, CreatedAt = now, StartedAt = now, LastSeenAt = now
            });
            await db.SaveChangesAsync();
        }
        var summary = (await PageAsync(board.Id)).Cards.Single();
        summary.HasMore.ShouldBeTrue();
        summary.Description.ShouldNotContain("LATE_MARKER");
        summary.TerminalReason!.ShouldNotContain("LATE_MARKER");
        summary.Sessions.Count.ShouldBe(0);
        using var client = _fixture.CreateClient();
        var detail = await client.GetFromJsonAsync<CardDto>($"/api/cards/{cards[0].Id}", Json);
        detail!.Description.ShouldContain("LATE_MARKER");
        detail.Sessions.Select(s => s.Id).ShouldContain(sessionId);
        var raw = await client.GetStringAsync($"/api/cards?boardId={board.Id}");
        using var json = JsonDocument.Parse(raw);
        json.RootElement.GetProperty("cards")[0].TryGetProperty("privateNotes", out _).ShouldBeFalse();
        raw.ShouldNotContain("private-marker-0846");
    }
}
