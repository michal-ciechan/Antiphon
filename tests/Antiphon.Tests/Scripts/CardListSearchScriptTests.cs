using System.Text.Json;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0846: the card command's list and search (Antiphon.Nightly.CardCli, which replaced scripts/card.ps1 in unit 27 of the
/// 2026-11-22 plan) follow every page and buffer the result, or fail without printing a partial one. In process against a
/// stub transport that records each request (path, query, task token, method, body); nothing leaves the process.
/// CardRelationshipScriptTests reuses the stub and the runner.
/// </summary>
[Category("Integration")]
public sealed class CardListSearchScriptTests
{
    private const string BoardId = "22222222-2222-2222-2222-222222222222";
    internal readonly record struct Reply(int Status, string Body);
    internal readonly record struct Call(string Path, string Query, string? TaskToken, string Method, string Body)
    {
        public string? Value(string key)
        {
            foreach (var part in Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = part.Split('=', 2);
                if (Uri.UnescapeDataString(pair[0]) == key)
                    return pair.Length == 2 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : "";
            }
            return null;
        }
    }

    /// <summary>The card API stand-in: /api/boards names one board, Antiphon; every other request is Respond's (default: an empty page).</summary>
    internal sealed class Stub(string prefix = "") : IDisposable
    {
        public readonly List<Call> Calls = [];
        public Func<Call, int, Reply>? Respond;
        public string BaseUrl { get; } = CardCommandRun.Api + prefix;

        public NightlyReportHttp Transport => (method, uri, headers, body) =>
        {
            var question = uri.IndexOf('?');
            var call = new Call(new Uri(uri).AbsolutePath, question < 0 ? "" : uri[question..],
                headers.TryGetValue("X-Antiphon-Task-Token", out var token) ? token : null, method, body ?? "");
            Calls.Add(call);
            Reply reply;
            if (call.Path.EndsWith("/api/boards", StringComparison.Ordinal))
                reply = new(200, $"[{{\"id\":\"{BoardId}\",\"name\":\"Antiphon\"}}]");
            else
                reply = Respond?.Invoke(call, Calls.Count) ?? new(200, Page(0, 0));
            return new NightlyReportHttpResponse(reply.Status, reply.Body);
        };

        public void Dispose() { }
    }

    private static string Page(int start, int count, bool more = false, string? token = null,
        long? total = null, int? duplicate = null)
    {
        var cards = Enumerable.Range(start, count).Select(i => new
        {
            id = $"00000000-0000-0000-0000-{(duplicate ?? i + 1):D12}",
            identifier = $"CARD-{i + 1:D4}", title = $"Card {i + 1}", status = "Backlog",
            importance = "Normal", urgency = "Normal", rank = 0,
            importanceProvenance = "Auto", boardId = BoardId, labels = Array.Empty<string>()
        }).ToArray();
        return JsonSerializer.Serialize(new { cards, total, truncated = more, nextPageToken = token });
    }

    /// <summary>The command line after "card" against the stub, with the task token the script tests set.</summary>
    internal static Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(Stub stub, params string[] args)
    {
        var run = CardCommandRun.Run(stub.Transport, args, taskToken: "synthetic-task-token", api: stub.BaseUrl);
        return Task.FromResult((run.Exit, run.Output, run.Error));
    }
    [Test] public async Task List_exhausts_pages_and_keeps_json_clean()
    {
        using var stub = new Stub();
        stub.Respond = (call, _) => call.Value("pageToken") is null
            ? new(200, Page(0, 500, true, "second")) : new(200, Page(500, 105));
        var run = await RunAsync(stub, "list", "--board", BoardId, "--json");
        run.ExitCode.ShouldBe(0, run.Stderr);
        using var json = JsonDocument.Parse(run.Stdout);
        json.RootElement.GetProperty("cards").GetArrayLength().ShouldBe(605);
        json.RootElement.GetProperty("total").GetInt64().ShouldBe(605);
        run.Stderr.ShouldContain("continuing");
        stub.Calls.Count.ShouldBe(2);
    }

    [Test] public async Task Search_uses_search_endpoint_and_preserves_encoded_query()
    {
        using var stub = new Stub();
        const string opaque = "next+/=%?&";
        stub.Respond = (call, _) => call.Value("pageToken") is null
            ? new(200, Page(0, 1, true, opaque, 2)) : new(200, Page(1, 1, total: 2));
        var phrase = "雪 +&%#/_\\ quote\"";
        var run = await RunAsync(stub, "search", phrase, "--board", BoardId, "--status", "Done", "--json");
        run.ExitCode.ShouldBe(0, run.Stderr);
        stub.Calls.All(c => c.Path == "/api/cards/search" && c.Value("q") == phrase &&
            c.Value("status") == "Done").ShouldBeTrue();
        stub.Calls[1].Value("pageToken").ShouldBe(opaque);
        JsonDocument.Parse(run.Stdout).RootElement.GetProperty("cards").GetArrayLength().ShouldBe(2);
    }

    [Test] public async Task All_includes_archives_and_resolves_archived_board_names()
    {
        using var stub = new Stub();
        stub.Respond = (call, _) => new(200, Page(0, 1, total: call.Path.EndsWith("search") ? 1 : null));
        var run = await RunAsync(stub, "list", "--board", "Antiphon", "--all", "--json");
        run.ExitCode.ShouldBe(0, run.Stderr);
        var search = await RunAsync(stub, "search", "needle", "--board", "Antiphon", "--all", "--json");
        search.ExitCode.ShouldBe(0, search.Stderr);
        stub.Calls.Count.ShouldBe(4);
        stub.Calls.All(c => c.Value("includeArchived") == "true").ShouldBeTrue();
        using var defaultStub = new Stub();
        defaultStub.Respond = (call, _) => new(200, Page(0, 1, total: call.Path.EndsWith("search") ? 1 : null));
        (await RunAsync(defaultStub, "search", "needle", "--board", "Antiphon", "--json")).ExitCode.ShouldBe(0);
        defaultStub.Calls.All(c => c.Value("includeArchived") is null).ShouldBeTrue();
    }

    [Test] public async Task Paging_is_automatic_without_All()
    {
        foreach (var all in new[] { false, true })
        foreach (var search in new[] { false, true })
        {
            using var stub = new Stub();
            stub.Respond = (call, _) => call.Value("pageToken") is null
                ? new(200, Page(0, 2, true, "next", search ? 3 : null))
                : new(200, Page(2, 1, total: search ? 3 : null));
            var args = search ? new List<string> { "search", "needle" } : new List<string> { "list", "--status", "Backlog" };
            args.AddRange(["--limit", "2"]);
            if (all) args.Add("--all");
            args.Add("--json");
            var run = await RunAsync(stub, args.ToArray());
            run.ExitCode.ShouldBe(0, run.Stderr);
            JsonDocument.Parse(run.Stdout).RootElement.GetProperty("cards").GetArrayLength().ShouldBe(3);
            stub.Calls.Count.ShouldBe(2);
            stub.Calls.All(c => c.Value("limit") == "2" &&
                c.Value("includeArchived") == (all ? "true" : null)).ShouldBeTrue();
        }
    }

    [Test] public async Task Old_or_inconsistent_pagination_fails_without_results()
    {
        foreach (var broken in new[] { Page(0, 1, true), Page(0, 1, true, " "),
            Page(0, 1, false, "unexpected") })
        {
            using var stub = new Stub();
            stub.Respond = (_, _) => new(200, broken);
            var run = await RunAsync(stub, "list", "--status", "Backlog", "--json");
            run.ExitCode.ShouldNotBe(0);
            run.Stdout.ShouldBeNullOrWhiteSpace();
            run.Stderr.ShouldContain("enumeration");
        }
        foreach (var (search, emptyOnSecondPage) in new[]
        {
            (false, false), (false, true), (true, false), (true, true)
        })
        {
            using var stub = new Stub();
            var endpoint = search ? "/api/cards/search" : "/api/cards";
            var total = search ? 2 : (int?)null;
            stub.Respond = (_, request) => request switch
            {
                1 when emptyOnSecondPage => new(200, Page(0, 1, true, "A", total)),
                1 => new(200, Page(0, 0, true, "A", search ? 1 : null)),
                2 when emptyOnSecondPage => new(200, Page(1, 0, true, "B", total)),
                2 => new(200, Page(0, 1, total: search ? 1 : null)),
                _ => new(200, Page(1, 1, total: total))
            };
            var args = search ? new[] { "search", "needle", "--json" } :
                new[] { "list", "--status", "Backlog", "--json" };
            var run = await RunAsync(stub, args);
            stub.Calls.Select(c => (c.Path, Token: c.Value("pageToken"))).ToArray()
                .ShouldBe(emptyOnSecondPage
                    ? new[] { (endpoint, (string?)null), (endpoint, "A") }
                    : new[] { (endpoint, (string?)null) });
            run.ExitCode.ShouldNotBe(0);
            run.Stdout.ShouldBeNullOrWhiteSpace();
            run.Stderr.ShouldContain("empty continuing page");
        }
        foreach (var tokens in new[] { new[] { "A", "A" }, new[] { "A", "B", "A" } })
        {
            using var cycle = new Stub();
            cycle.Respond = (_, request) => request <= tokens.Length
                ? new(200, Page(request - 1, 1, true, tokens[request - 1]))
                : new(200, Page(request - 1, 1));
            var repeated = await RunAsync(cycle, "list", "--status", "Backlog", "--json");
            cycle.Calls.Select(c => (c.Path, Token: c.Value("pageToken"))).ToArray()
                .ShouldBe(tokens.Length == 2
                    ? new[] { ("/api/cards", (string?)null), ("/api/cards", "A") }
                    : new[] { ("/api/cards", (string?)null), ("/api/cards", "A"),
                        ("/api/cards", "B") });
            repeated.ExitCode.ShouldNotBe(0);
            repeated.Stdout.ShouldBeNullOrWhiteSpace();
            repeated.Stderr.ShouldContain("page token");
        }
        using var caseSensitive = new Stub();
        caseSensitive.Respond = (_, request) => request switch
        {
            1 => new(200, Page(0, 1, true, "A")),
            2 => new(200, Page(1, 1, true, "a")),
            _ => new(200, Page(2, 1))
        };
        (await RunAsync(caseSensitive, "list", "--status", "Backlog", "--json")).ExitCode.ShouldBe(0);
        caseSensitive.Calls.Count.ShouldBe(3);
    }

    [Test] public async Task Later_http_failure_and_changed_scope_do_not_emit_partial_json()
    {
        using (var listFailure = new Stub())
        {
            listFailure.Respond = (call, _) => call.Value("pageToken") is null
                ? new(200, Page(0, 1, true, "next"))
                : new(500, "{\"detail\":\"server failed; restart enumeration\"}");
            var listRun = await RunAsync(listFailure, "list", "--status", "Backlog", "--json");
            listFailure.Calls.Select(c => (c.Path, Token: c.Value("pageToken"))).ToArray()
                .ShouldBe(new[] { ("/api/cards", (string?)null), ("/api/cards", "next") });
            listRun.ExitCode.ShouldNotBe(0);
            listRun.Stdout.ShouldBeNullOrWhiteSpace();
            listRun.Stderr.ShouldContain("restart enumeration");
        }
        foreach (var status in new[] { 409, 500 })
        {
            using var stub = new Stub();
            stub.Respond = (call, _) => call.Value("pageToken") is null
                ? new(200, Page(0, 1, true, "next", 2))
                : new(status, "{\"code\":\"card_page_changed\",\"detail\":\"restart without pageToken\"}");
            var run = await RunAsync(stub, "search", "needle", "--json");
            run.ExitCode.ShouldNotBe(0);
            run.Stdout.ShouldBeNullOrWhiteSpace();
            run.Stderr.ShouldContain("restart");
            stub.Calls.Count.ShouldBe(2);
        }
        using var missingSearch = new Stub();
        missingSearch.Respond = (_, _) => new(404, "{\"detail\":\"no search route\"}");
        var missing = await RunAsync(missingSearch, "search", "needle", "--json");
        missing.ExitCode.ShouldNotBe(0);
        missing.Stdout.ShouldBeNullOrWhiteSpace();
        missingSearch.Calls.Count.ShouldBe(1);
        missingSearch.Calls[0].Path.ShouldBe("/api/cards/search");
    }

    [Test] public async Task Duplicate_ids_and_inconsistent_totals_are_errors()
    {
        foreach (var second in new[] { Page(1, 1, total: 2, duplicate: 1),
            Page(1, 0, total: 2) })
        {
            using var stub = new Stub();
            stub.Respond = (call, _) => call.Value("pageToken") is null
                ? new(200, Page(0, 1, true, "next", 2)) : new(200, second);
            var run = await RunAsync(stub, "search", "needle", "--json");
            run.ExitCode.ShouldNotBe(0);
            run.Stdout.ShouldBeNullOrWhiteSpace();
        }
        foreach (var (firstTotal, secondTotal) in new[] { (2, 3), (3, 2) })
        {
            using var changedTotal = new Stub();
            changedTotal.Respond = (call, _) => call.Value("pageToken") is null
                ? new(200, Page(0, 1, true, "next", firstTotal))
                : new(200, Page(1, 1, total: secondTotal));
            var changed = await RunAsync(changedTotal, "search", "needle", "--json");
            changedTotal.Calls.Select(c => c.Value("pageToken")).ToArray()
                .ShouldBe(new string?[] { null, "next" });
            changed.ExitCode.ShouldNotBe(0);
            changed.Stdout.ShouldBeNullOrWhiteSpace();
            changed.Stderr.ShouldContain("total changed");
        }
        using var within = new Stub();
        within.Respond = (_, _) => new(200, Page(0, 2, total: 2, duplicate: 1));
        var duplicate = await RunAsync(within, "search", "needle", "--json");
        duplicate.ExitCode.ShouldNotBe(0);
        duplicate.Stderr.ShouldContain("repeated a card id");
        using var greater = new Stub();
        greater.Respond = (call, _) => call.Value("pageToken") is null
            ? new(200, Page(0, 1, true, "next", 1)) : new(200, Page(1, 1, total: 1));
        var tooMany = await RunAsync(greater, "search", "needle", "--json");
        tooMany.ExitCode.ShouldNotBe(0);
        tooMany.Stderr.ShouldContain("final count differs");
    }

    [Test] public async Task Collection_argument_errors_are_local()
    {
        foreach (var args in new[] { new[] { "list" }, new[] { "list", "--all" },
            new[] { "search" }, new[] { "search", " ", "--board", "Antiphon" },
            new[] { "list", "CARD-1", "--status", "Done", "--board", "Antiphon" },
            new[] { "list", "--status", "Done", "--limit", "0" },
            new[] { "list", "--board", "Antiphon", "--updated-since", "nonsense" },
            new[] { "list", "--board", "Antiphon", "--status", "NoSuchStatus" },
            new[] { "search", "needle", "--updated-since", "2026-09-30T00:00:00Z" },
            new[] { "get", "CARD-1", "--all" } })
        {
            using var stub = new Stub();
            var run = await RunAsync(stub, args);
            run.ExitCode.ShouldNotBe(0, string.Join(' ', args));
            stub.Calls.Count.ShouldBe(0, string.Join(' ', args));
        }
    }

    [Test] public async Task Empty_and_single_results_keep_array_shapes()
    {
        foreach (var count in new[] { 0, 1 })
        {
            using var stub = new Stub();
            stub.Respond = (call, _) => new(200, Page(0, count, total: call.Path.EndsWith("search") ? count : null));
            foreach (var args in new[] { new[] { "list", "--status", "Done", "--json" },
                new[] { "search", "needle", "--json" } })
            {
                var run = await RunAsync(stub, args);
                run.ExitCode.ShouldBe(0, run.Stderr);
                JsonDocument.Parse(run.Stdout).RootElement.GetProperty("cards").GetArrayLength().ShouldBe(count);
            }
        }
        using var humanStub = new Stub();
        humanStub.Respond = (_, _) => new(200, Page(0, 1));
        var human = await RunAsync(humanStub, "list", "--status", "Done");
        human.ExitCode.ShouldBe(0, human.Stderr);
        human.Stdout.ShouldContain("1 cards in all boards");
        human.Stdout.ShouldContain("previewed");
        human.Stdout.ShouldContain("scripts/antiphon card get <hit>");
        human.Stdout.ShouldContain(BoardId);
    }

    [Test] public async Task Every_page_preserves_base_and_task_header()
    {
        using var stub = new Stub("/fixture");
        stub.Respond = (call, _) => call.Value("pageToken") is null
            ? new(200, Page(0, 1, true, "next")) : new(200, Page(1, 1));
        var run = await RunAsync(stub, "list", "--board", "Antiphon", "--status", "Done", "--json");
        run.ExitCode.ShouldBe(0, run.Stderr);
        stub.Calls.Count.ShouldBe(3);
        stub.Calls.All(c => c.Path.StartsWith("/fixture/api/", StringComparison.Ordinal)).ShouldBeTrue();
        stub.Calls.All(c => c.TaskToken == "synthetic-task-token").ShouldBeTrue();
    }

    [Test] public async Task List_updated_since_is_encoded_and_preserved()
    {
        using var stub = new Stub();
        stub.Respond = (call, _) => call.Value("pageToken") is null
            ? new(200, Page(0, 1, true, "next")) : new(200, Page(1, 1));
        const string since = "2026-09-30T00:00:00Z";
        var run = await RunAsync(stub, "list", "--updated-since", since, "--json");
        run.ExitCode.ShouldBe(0, run.Stderr);
        stub.Calls.Count.ShouldBe(2);
        stub.Calls.All(c => c.Value("updatedSince") == since).ShouldBeTrue();
        stub.Calls[1].Value("pageToken").ShouldBe("next");
    }
}
