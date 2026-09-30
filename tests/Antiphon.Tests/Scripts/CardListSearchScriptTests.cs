using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CardListSearchScriptTests
{
    private const string BoardId = "22222222-2222-2222-2222-222222222222";
    private readonly record struct Reply(int Status, string Body);
    private readonly record struct Call(string Path, string Query, string? TaskToken)
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

    private sealed class Stub : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _pump;
        public readonly List<Call> Calls = [];
        public Func<Call, int, Reply>? Respond;
        public string BaseUrl { get; }

        public Stub(string prefix = "")
        {
            BaseUrl = EphemeralHttpListener.BindLoopback(_listener).TrimEnd('/') + prefix;
            _pump = Task.Run(PumpAsync);
        }

        private async Task PumpAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch { return; }
                var request = context.Request;
                var call = new Call(request.Url!.AbsolutePath, request.Url.Query, request.Headers["X-Antiphon-Task-Token"]);
                Calls.Add(call);
                Reply reply;
                if (call.Path.EndsWith("/api/boards", StringComparison.Ordinal))
                    reply = new(200, $"[{{\"id\":\"{BoardId}\",\"name\":\"Antiphon\"}}]");
                else
                    reply = Respond?.Invoke(call, Calls.Count) ?? new(200, Page(0, 0));
                var bytes = Encoding.UTF8.GetBytes(reply.Body);
                context.Response.StatusCode = reply.Status;
                context.Response.ContentType = "application/json";
                try { await context.Response.OutputStream.WriteAsync(bytes); }
                catch (HttpListenerException) { }
                finally { context.Response.Close(); }
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            try { _listener.Stop(); } catch { }
            try { _pump.Wait(TimeSpan.FromSeconds(5)); } catch { }
            _listener.Close();
            _stop.Dispose();
        }
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

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(Stub stub, params string[] args)
    {
        var start = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "card.ps1"));
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment["ANTIPHON_API"] = stub.BaseUrl.TrimEnd('/');
        start.Environment["ANTIPHON_TASK_TOKEN"] = "synthetic-task-token";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        return (process.ExitCode, await stdout, await stderr);
    }

    [Test] public async Task List_exhausts_pages_and_keeps_json_clean()
    {
        using var stub = new Stub();
        stub.Respond = (call, _) => call.Value("pageToken") is null
            ? new(200, Page(0, 500, true, "second")) : new(200, Page(500, 105));
        var run = await RunAsync(stub, "list", "-Board", BoardId, "-Json");
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
        var run = await RunAsync(stub, "search", phrase, "-Board", BoardId, "-Status", "Done", "-Json");
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
        var run = await RunAsync(stub, "list", "-Board", "Antiphon", "-All", "-Json");
        run.ExitCode.ShouldBe(0, run.Stderr);
        var search = await RunAsync(stub, "search", "needle", "-Board", "Antiphon", "-All", "-Json");
        search.ExitCode.ShouldBe(0, search.Stderr);
        stub.Calls.Count.ShouldBe(4);
        stub.Calls.All(c => c.Value("includeArchived") == "true").ShouldBeTrue();
        using var defaultStub = new Stub();
        defaultStub.Respond = (call, _) => new(200, Page(0, 1, total: call.Path.EndsWith("search") ? 1 : null));
        (await RunAsync(defaultStub, "search", "needle", "-Board", "Antiphon", "-Json")).ExitCode.ShouldBe(0);
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
            var args = search ? new List<string> { "search", "needle" } : new List<string> { "list", "-Status", "Backlog" };
            args.AddRange(["-Limit", "2"]);
            if (all) args.Add("-All");
            args.Add("-Json");
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
            Page(0, 1, false, "unexpected"), Page(0, 0, true, "next") })
        {
            using var stub = new Stub();
            stub.Respond = (_, _) => new(200, broken);
            var run = await RunAsync(stub, "list", "-Status", "Backlog", "-Json");
            run.ExitCode.ShouldNotBe(0);
            run.Stdout.ShouldBeNullOrWhiteSpace();
            run.Stderr.ShouldContain("enumeration");
        }
        foreach (var tokens in new[] { new[] { "A", "A" }, new[] { "A", "B", "A" } })
        {
            using var cycle = new Stub();
            cycle.Respond = (_, request) => new(200, Page(request - 1, 1, true,
                tokens[Math.Min(request - 1, tokens.Length - 1)]));
            var repeated = await RunAsync(cycle, "list", "-Status", "Backlog", "-Json");
            repeated.ExitCode.ShouldNotBe(0);
            repeated.Stdout.ShouldBeNullOrWhiteSpace();
            repeated.Stderr.ShouldContain("page token");
            cycle.Calls.Count.ShouldBe(tokens.Length);
        }
        using var caseSensitive = new Stub();
        caseSensitive.Respond = (_, request) => request switch
        {
            1 => new(200, Page(0, 1, true, "A")),
            2 => new(200, Page(1, 1, true, "a")),
            _ => new(200, Page(2, 1))
        };
        (await RunAsync(caseSensitive, "list", "-Status", "Backlog", "-Json")).ExitCode.ShouldBe(0);
        caseSensitive.Calls.Count.ShouldBe(3);
    }

    [Test] public async Task Later_http_failure_and_changed_scope_do_not_emit_partial_json()
    {
        foreach (var status in new[] { 409, 500 })
        {
            using var stub = new Stub();
            stub.Respond = (call, _) => call.Value("pageToken") is null
                ? new(200, Page(0, 1, true, "next", 2))
                : new(status, "{\"code\":\"card_page_changed\",\"detail\":\"restart without pageToken\"}");
            var run = await RunAsync(stub, "search", "needle", "-Json");
            run.ExitCode.ShouldNotBe(0);
            run.Stdout.ShouldBeNullOrWhiteSpace();
            run.Stderr.ShouldContain("restart");
            stub.Calls.Count.ShouldBe(2);
        }
        using var missingSearch = new Stub();
        missingSearch.Respond = (_, _) => new(404, "{\"detail\":\"no search route\"}");
        var missing = await RunAsync(missingSearch, "search", "needle", "-Json");
        missing.ExitCode.ShouldNotBe(0);
        missing.Stdout.ShouldBeNullOrWhiteSpace();
        missingSearch.Calls.Count.ShouldBe(1);
        missingSearch.Calls[0].Path.ShouldBe("/api/cards/search");
    }

    [Test] public async Task Duplicate_ids_and_inconsistent_totals_are_errors()
    {
        foreach (var second in new[] { Page(1, 1, total: 2, duplicate: 1),
            Page(1, 1, total: 3), Page(1, 0, total: 2) })
        {
            using var stub = new Stub();
            stub.Respond = (call, _) => call.Value("pageToken") is null
                ? new(200, Page(0, 1, true, "next", 2)) : new(200, second);
            var run = await RunAsync(stub, "search", "needle", "-Json");
            run.ExitCode.ShouldNotBe(0);
            run.Stdout.ShouldBeNullOrWhiteSpace();
        }
        using var within = new Stub();
        within.Respond = (_, _) => new(200, Page(0, 2, total: 2, duplicate: 1));
        var duplicate = await RunAsync(within, "search", "needle", "-Json");
        duplicate.ExitCode.ShouldNotBe(0);
        duplicate.Stderr.ShouldContain("repeated a card id");
        using var greater = new Stub();
        greater.Respond = (call, _) => call.Value("pageToken") is null
            ? new(200, Page(0, 1, true, "next", 1)) : new(200, Page(1, 1, total: 1));
        var tooMany = await RunAsync(greater, "search", "needle", "-Json");
        tooMany.ExitCode.ShouldNotBe(0);
        tooMany.Stderr.ShouldContain("final count differs");
    }

    [Test] public async Task Collection_argument_errors_are_local()
    {
        foreach (var args in new[] { new[] { "list" }, new[] { "list", "-All" },
            new[] { "search" }, new[] { "search", " ", "-Board", "Antiphon" },
            new[] { "list", "CARD-1", "-Status", "Done", "-Board", "Antiphon" },
            new[] { "list", "-Status", "Done", "-Limit", "0" },
            new[] { "list", "-Board", "Antiphon", "-UpdatedSince", "nonsense" },
            new[] { "list", "-Board", "Antiphon", "-Status", "NoSuchStatus" },
            new[] { "search", "needle", "-UpdatedSince", "2026-09-30T00:00:00Z" },
            new[] { "get", "CARD-1", "-All" } })
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
            foreach (var args in new[] { new[] { "list", "-Status", "Done", "-Json" },
                new[] { "search", "needle", "-Json" } })
            {
                var run = await RunAsync(stub, args);
                run.ExitCode.ShouldBe(0, run.Stderr);
                JsonDocument.Parse(run.Stdout).RootElement.GetProperty("cards").GetArrayLength().ShouldBe(count);
            }
        }
        using var humanStub = new Stub();
        humanStub.Respond = (_, _) => new(200, Page(0, 1));
        var human = await RunAsync(humanStub, "list", "-Status", "Done");
        human.ExitCode.ShouldBe(0, human.Stderr);
        human.Stdout.ShouldContain("1 cards in all boards");
        human.Stdout.ShouldContain("previewed");
        human.Stdout.ShouldContain("card.ps1 get");
        human.Stdout.ShouldContain(BoardId);
    }

    [Test] public async Task Every_page_preserves_base_and_task_header()
    {
        using var stub = new Stub("/fixture");
        stub.Respond = (call, _) => call.Value("pageToken") is null
            ? new(200, Page(0, 1, true, "next")) : new(200, Page(1, 1));
        var run = await RunAsync(stub, "list", "-Board", "Antiphon", "-Status", "Done", "-Json");
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
        var run = await RunAsync(stub, "list", "-UpdatedSince", since, "-Json");
        run.ExitCode.ShouldBe(0, run.Stderr);
        stub.Calls.Count.ShouldBe(2);
        stub.Calls.All(c => c.Value("updatedSince") == since).ShouldBeTrue();
        stub.Calls[1].Value("pageToken").ShouldBe("next");
    }
}
