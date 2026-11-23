using System.Text.Json;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Scripts.CardListSearchScriptTests;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0100 S4: the card command's block, unblock, get and history (Antiphon.Nightly.CardCli, which replaced scripts/card.ps1
/// in unit 27 of the 2026-11-22 plan) against the list/search stub transport, in process. Not ported:
/// Block_404_detail_stays_intact_when_concise_view_wraps_at_120_columns (pwsh's ConciseView rendering in a 120-column Linux
/// pty; the command writes the detail as plain text, which Block_fails_loudly_when_relationships_returns_404 pins line by
/// line). The two in-process 404 rows keep their names with the C# caller's meaning: exit 1, and no exception.
/// </summary>
[Category("Integration")]
public sealed class CardRelationshipScriptTests
{
    private const string CardJson = """
        {"id":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee","identifier":"CARD-0051","status":"Backlog",
         "title":"Blocked card","importance":"Normal","urgency":"Normal","rank":0,
         "concurrencyToken":"11111111-1111-1111-1111-111111111111","revisionCount":1,
         "boardId":"22222222-2222-2222-2222-222222222222","labels":[],
         "blockers":[{"identifier":"CARD-0049","status":"InProgress","resolved":false},
                     {"identifier":"CARD-0048","status":"Done","resolved":true}]}
        """;
    private const string RelationshipsJson = """
        {"blockedBy":[
          {"id":"44444444-4444-4444-4444-444444444444","kind":"BlockedBy","relatedIdentifier":"CARD-0049",
           "relatedTitle":"CARD-0050 lookalike","relatedStatus":"InProgress","resolved":false},
          {"id":"55555555-5555-5555-5555-555555555555","kind":"BlockedBy","relatedIdentifier":"CARD-0048",
           "relatedTitle":"CARD-0049","relatedStatus":"Done","resolved":true}],
         "blocks":[{"relatedIdentifier":"CARD-0052"}]}
        """;

    private static Stub CreateStub() => new()
    {
        Respond = (call, _) => (call.Method, call.Path) switch
        {
            ("GET", "/api/cards/limits") => new(200,
                """{"maxReasonLength":4000,"maxActorLength":200}"""),
            ("GET", "/api/cards/CARD-0051") => new(200, CardJson),
            ("GET", "/api/cards/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/relationships") => new(200, RelationshipsJson),
            ("GET", "/api/cards/CARD-0051/revisions") => new(200,
                """[{"revisionNumber":1,"kind":"RelationshipAdded","relatedCardIdentifier":"CARD-0049","reason":"schema first","createdAt":"2026-10-14T00:00:00Z"}]"""),
            ("POST", "/api/cards/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/relationships") => new(201,
                """{"id":"44444444-4444-4444-4444-444444444444","relatedIdentifier":"CARD-0049","relatedStatus":"InProgress","resolved":false,"cardConcurrencyToken":"66666666-6666-6666-6666-666666666666"}"""),
            ("POST", "/api/cards/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/relationships/44444444-4444-4444-4444-444444444444/remove") => new(200, CardJson),
            _ => new(404, """{"detail":"Unexpected request"}""")
        }
    };

    [Test]
    public async Task Block_reads_the_card_then_posts_the_relationship_body_with_the_fetched_token()
    {
        using var stub = CreateStub();
        var run = await RunAsync(stub, "block", "CARD-0051", "--blocked-by", "CARD-0049",
            "--reason", "schema first", "--by", "operator");
        run.ExitCode.ShouldBe(0, run.Stderr);
        stub.Calls.Where(c => c.Path != "/api/cards/limits").Select(c => $"{c.Method} {c.Path}")
            .ShouldBe(new[] { "GET /api/cards/CARD-0051", "POST /api/cards/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/relationships" });
        using var body = JsonDocument.Parse(stub.Calls.Single(c => c.Method == "POST").Body);
        body.RootElement.EnumerateObject().Select(p => p.Name).Order().ShouldBe(
            new[] { "concurrencyToken", "createdBy", "kind", "reason", "relatedCard" });
        body.RootElement.GetProperty("concurrencyToken").GetString().ShouldBe("11111111-1111-1111-1111-111111111111");
        body.RootElement.GetProperty("relatedCard").GetString().ShouldBe("CARD-0049");
        body.RootElement.GetProperty("kind").GetString().ShouldBe("BlockedBy");
        body.RootElement.GetProperty("reason").GetString().ShouldBe("schema first");
        body.RootElement.GetProperty("createdBy").GetString().ShouldBe("operator");
        run.Stdout.ShouldContain("blocked by  CARD-0049");

        // The file path must preserve text, and the explicit token and board fence must reach the API.
        var reasonFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(reasonFile, "schema first\nsecond line 雪");
            stub.Calls.Clear();
            var fenced = await RunAsync(stub, "block", "CARD-0051", "--blocked-by", "CARD-0049",
                "--reason-file", reasonFile, "--token", "77777777-7777-7777-7777-777777777777", "--related-board", "Antiphon", "--json");
            fenced.ExitCode.ShouldBe(0, fenced.Stderr);
            using var fencedBody = JsonDocument.Parse(stub.Calls.Single(c => c.Method == "POST").Body);
            fencedBody.RootElement.GetProperty("reason").GetString().ShouldBe("schema first\nsecond line 雪");
            fencedBody.RootElement.GetProperty("concurrencyToken").GetString().ShouldBe("77777777-7777-7777-7777-777777777777");
            fencedBody.RootElement.GetProperty("relatedBoardId").GetString().ShouldBe("22222222-2222-2222-2222-222222222222");
            using var response = JsonDocument.Parse(fenced.Stdout);
            response.RootElement.GetProperty("cardConcurrencyToken").GetString().ShouldBe("66666666-6666-6666-6666-666666666666");
        }
        finally { File.Delete(reasonFile); }
    }

    [Test]
    public async Task Unblock_resolves_the_row_by_relatedIdentifier_and_posts_remove()
    {
        using var stub = CreateStub();
        var run = await RunAsync(stub, "unblock", "CARD-0051", "--blocked-by", "CARD-0049");
        run.ExitCode.ShouldBe(0, run.Stderr);
        stub.Calls.Select(c => $"{c.Method} {c.Path}").ShouldBe(new[]
        {
            "GET /api/cards/CARD-0051",
            "GET /api/cards/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/relationships",
            "POST /api/cards/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/relationships/44444444-4444-4444-4444-444444444444/remove"
        });
        var remove = stub.Calls.Single(c => c.Method == "POST");
        remove.Path.ShouldEndWith("/relationships/44444444-4444-4444-4444-444444444444/remove");
        using var body = JsonDocument.Parse(remove.Body);
        body.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(new[] { "concurrencyToken" });
        body.RootElement.GetProperty("concurrencyToken").GetString().ShouldBe("11111111-1111-1111-1111-111111111111");
    }

    [Test]
    public async Task Get_and_history_print_blocked_by_blocks_and_relationship_rows()
    {
        using var stub = CreateStub();
        var get = await RunAsync(stub, "get", "CARD-0051");
        get.ExitCode.ShouldBe(0, get.Stderr);
        get.Stdout.ShouldContain("blocked by  CARD-0049 (InProgress)");
        get.Stdout.ShouldContain("blocked by  CARD-0048 (resolved: Done)");
        get.Stdout.ShouldContain("blocks      CARD-0052");
        var history = await RunAsync(stub, "history", "CARD-0051");
        history.ExitCode.ShouldBe(0, history.Stderr);
        history.Stdout.Split('\n').ShouldContain(line => line.Contains("RelationshipAdded") && line.Contains("-> CARD-0049"));
        stub.Calls.Clear();
        var json = await RunAsync(stub, "get", "CARD-0051", "--json");
        json.ExitCode.ShouldBe(0, json.Stderr);
        JsonElement.DeepEquals(JsonDocument.Parse(json.Stdout).RootElement, JsonDocument.Parse(CardJson).RootElement).ShouldBeTrue();
        stub.Calls.Select(c => c.Path).ShouldBe(new[] { "/api/cards/CARD-0051" });
    }

    [Test]
    public async Task Get_prints_the_card_when_relationships_returns_an_empty_404()
    {
        using var stub = CreateStub();
        var respond = stub.Respond!;
        stub.Respond = (call, index) => call.Path.EndsWith("/relationships")
            ? new(404, "") : respond(call, index);

        var run = await RunAsync(stub, "get", "CARD-0051");

        run.ExitCode.ShouldBe(0, run.Stderr);
        AssertCardWithoutRelationships(run.Stdout);
        run.Stderr.ShouldBeEmpty();
        stub.Calls.Select(c => $"{c.Method} {c.Path}").ShouldBe(new[]
        {
            "GET /api/cards/CARD-0051",
            "GET /api/cards/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/relationships"
        });
    }

    [Test]
    public async Task Get_prints_the_card_and_warns_when_relationships_returns_500()
    {
        using var stub = CreateStub();
        var respond = stub.Respond!;
        stub.Respond = (call, index) => call.Path.EndsWith("/relationships")
            ? new(500, """{"detail":"Relationship store unavailable"}""") : respond(call, index);

        var run = await RunAsync(stub, "get", "CARD-0051");

        run.ExitCode.ShouldBe(0, run.Stderr);
        AssertCardWithoutRelationships(run.Stdout);
        run.Stderr.Trim().ShouldBe("WARNING: relationships lookup failed (HTTP 500).");
    }

    [Test]
    public async Task Block_fails_loudly_when_relationships_returns_404()
    {
        using var stub = CreateStub();
        var respond = stub.Respond!;
        stub.Respond = (call, index) => call.Path.EndsWith("/relationships")
            ? new(404, """{"detail":"Relationship route unavailable","code":"relationship_route_missing"}""")
            : respond(call, index);

        var run = await RunAsync(stub, "block", "CARD-0051", "--blocked-by", "CARD-0049");

        run.ExitCode.ShouldNotBe(0);
        var error = System.Text.RegularExpressions.Regex.Replace(run.Stderr, @"\s+", " ");
        error.ShouldContain("Relationship route unavailable");
        error.ShouldContain("code relationship_route_missing");
        // The detail stays intact on its own line, the code on the next: plain text, no console view in between.
        run.Stderr.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ShouldBe(new[]
        {
            "Antiphon POST /api/cards/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/relationships failed: Relationship route unavailable",
            "code relationship_route_missing",
        });
        run.Stdout.ShouldBeEmpty();
        stub.Calls.Select(c => $"{c.Method} {c.Path}").ShouldBe(new[]
        {
            "GET /api/cards/CARD-0051",
            "POST /api/cards/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/relationships"
        });
    }

    // A 404 stops the command: exit 1, nothing on stdout, the server's detail on stderr (the script's pwsh -Command chain
    // stopped at the throw; the command answers the exit code).
    [Test]
    public async Task Get_404_stops_an_in_process_command_chain()
    {
        using var stub = CreateStub();
        var run = await RunAsync(stub, "get", "CARD-9999");
        run.ExitCode.ShouldBe(1, run.Stdout + "\n" + run.Stderr);
        run.Stdout.ShouldBeEmpty();
        run.Stderr.ShouldContain("Unexpected request");
        stub.Calls.Select(c => c.Path).ShouldBe(new[] { "/api/cards/CARD-9999" });
    }

    // An in-process caller (SessionRunner, a remote host) gets the exit code back from CardCli.Run, never an exception (the
    // script's 404 was a terminating error a try could catch).
    [Test]
    public void Get_404_is_caught_by_an_in_process_try()
    {
        using var stub = CreateStub();
        var output = new StringWriter();
        var error = new StringWriter();
        var options = CardCliOptions.Parse(["get", "CARD-9999"], stub.BaseUrl, "synthetic-task-token", CardCommandRun.WorkingDirectory);
        var exit = Should.NotThrow(() => CardCli.Run(options, CardCommandRun.Host(output, error), new System.IO.Abstractions.FileSystem(), stub.Transport));
        exit.ShouldBe(1);
        output.ToString().ShouldBeEmpty();
        error.ToString().ShouldContain("Unexpected request");
    }
    private static void AssertCardWithoutRelationships(string stdout)
    {
        stdout.ShouldContain("CARD-0051");
        stdout.ShouldContain("Blocked card");
        stdout.ShouldContain("id          aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        stdout.ShouldContain("board       22222222-2222-2222-2222-222222222222");
        stdout.ShouldContain("importance  Normal");
        stdout.ShouldContain("urgency     Normal");
        stdout.ShouldContain("rank        0");
        stdout.ShouldContain("token       11111111-1111-1111-1111-111111111111");
        stdout.ShouldContain("revisions   1");
        stdout.Replace("\r\n", "\n").Split('\n')
            .Where(line => line.StartsWith("relationships") || line.StartsWith("blocked by") || line.StartsWith("blocks"))
            .ShouldBe(new[] { "relationships: unavailable" });
    }

    [Test]
    public async Task Unblock_without_a_matching_blocker_exits_non_zero_and_names_the_current_blockers()
    {
        using var stub = CreateStub();
        var run = await RunAsync(stub, "unblock", "CARD-0051", "--blocked-by", "CARD-0047");
        run.ExitCode.ShouldNotBe(0);
        run.Stderr.ShouldContain("CARD-0049");
        run.Stderr.ShouldContain("CARD-0048");
        stub.Calls.ShouldNotContain(c => c.Method == "POST");
        stub.Calls.ShouldContain(c => c.Path.EndsWith("/relationships"));
    }
}
