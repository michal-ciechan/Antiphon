using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The card command (Antiphon.Nightly.CardCli, which replaced scripts/card.ps1 in unit 27 of the 2026-11-22 plan) in process
/// against a fake card API: the lines get, history and limits print, the checkout sent as cwd (git, then the workspace
/// marker, then the working directory), board names, column resolution and the spawn notice, the reopen, archive, reorder
/// and order bodies, the local refusals (nothing to change, over-long text, the alias rules, a missing file) and the
/// command-line errors. The script's own drivers are the Card*ScriptTests; these cover what they never did. Nothing leaves
/// the process.
/// </summary>
[Category("Integration")]
public sealed class CardCommandTests
{
    private const string CardId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string BoardId = "22222222-2222-2222-2222-222222222222";
    private const string Token = "11111111-1111-1111-1111-111111111111";

    [Test]
    public void Get_prints_every_field_in_order()
    {
        File.ReadAllBytes(Path.Combine(Antiphon.Tests.Application.DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "CardCli.cs")).ShouldAllBe(b => b < 128);
        var api = new CardApiFake
        {
            Card = new JsonObject
            {
                ["id"] = CardId, ["identifier"] = "CARD-0051", ["status"] = "Backlog", ["title"] = "Fix the thing", ["importance"] = "High",
                ["urgency"] = "Soon", ["rank"] = 2, ["position"] = 3, ["importanceProvenance"] = "Human", ["labels"] = new JsonArray("bug", "ui"),
                ["requiredPlatform"] = "Linux", ["boardId"] = BoardId, ["boardColumnId"] = "33333333-3333-3333-3333-333333333333", ["alias"] = "Thing fix",
                ["dueAt"] = "2026-12-01T00:00:00Z", ["concurrencyToken"] = Token, ["revisionCount"] = 4, ["assignedAgentName"] = "worker-1",
                ["ownerSessionId"] = "s-1", ["externalIssue"] = new JsonObject { ["key"] = "#12", ["needsHumanReview"] = true, ["author"] = null },
                ["archivedAt"] = "2026-11-20T00:00:00Z", ["archivedReason"] = "Filed away", ["terminalReason"] = "Done because", ["description"] = "Line one\nLine two",
            },
        };
        var run = CardCommandRun.Run(api.Http.Transport, ["get", "CARD-0051"]);
        run.Exit.ShouldBe(0, run.All);
        run.Error.ShouldBeEmpty();
        Lines(run.Output).ShouldBe(
        [
            "CARD-0051  Backlog  High/Soon  rank 2 pos 3 (human-rated)  Fix the thing [bug, ui]",
            "platform    Linux",
            "id          " + CardId,
            "board       " + BoardId,
            "column      33333333-3333-3333-3333-333333333333",
            "importance  High",
            "urgency     Soon",
            "alias       Thing fix",
            "rank        2",
            "pos         3",
            "due         2026-12-01T00:00:00Z",
            "token       " + Token,
            "revisions   4",
            "blocked by  CARD-0049 (InProgress)",
            "blocks      CARD-0052",
            "agent       worker-1",
            "session     s-1",
            "external    #12 [needs human review: raised by unknown]",
            "ARCHIVED    Filed away",
            "closed      Done because",
            "",
            "Line one",
            "Line two",
        ]);
    }

    [Test]
    public void History_prints_aligned_rows_with_the_related_card_and_the_author()
    {
        var api = new CardApiFake();
        api.Answers["GET /api/cards/CARD-0051/revisions"] = """
            [{"revisionNumber":1,"kind":"Create","createdAt":"2026-11-01T10:00:00Z","reason":"filed"},
             {"revisionNumber":12,"kind":"RelationshipAdded","relatedCardIdentifier":"CARD-0049","createdAt":"2026-11-02T10:00:00Z","reason":"schema first","editedBy":"operator"}]
            """;
        var run = CardCommandRun.Run(api.Http.Transport, ["history", "CARD-0051"]);
        run.Exit.ShouldBe(0, run.All);
        Lines(run.Output).ShouldBe(
        [
            "  1  Create       2026-11-01T10:00:00Z  filed",
            " 12  RelationshipAdded -> CARD-0049 2026-11-02T10:00:00Z  schema first",
            "     by operator",
        ]);

        api.Answers["GET /api/cards/CARD-0051/revisions"] = "[]";
        Lines(CardCommandRun.Run(api.Http.Transport, ["history", "CARD-0051"]).Output).ShouldBe(["No history."]);
    }

    [Test]
    public void Limits_prints_each_ceiling_and_sends_nothing_else()
    {
        var api = new CardApiFake();
        var run = CardCommandRun.Run(api.Http.Transport, ["limits"]);
        run.Exit.ShouldBe(0, run.All);
        Lines(run.Output).ShouldBe(
        [
            "title       300",
            "description 20000",
            "privateNotes 20000",
            "reason      4000",
            "actor       200",
            "alias       64 (5 words)",
            "importance  Low, Normal, High, Critical",
            "urgency     Normal, Soon, Now",
        ]);
        api.Requests.ShouldBe(["GET /api/cards/limits"]);
    }

    [Test]
    public void The_checkout_is_git_top_level_else_the_workspace_marker_else_the_working_directory()
    {
        var git = new FakeGit().Reply("rev-parse", new NightlyGitResult(0, "C:/src/antiphon\n"));
        var top = new CardApiFake();
        CardCommandRun.Run(top.Http.Transport, ["get", "CARD-0051", "--json"], git: git.Run, workingDirectory: "C:/src/antiphon/server").Exit.ShouldBe(0);
        top.Query("GET /api/cards/CARD-0051", "cwd").ShouldBe("C:/src/antiphon");
        git.Calls.Single().WorkingDirectory.ShouldBe("C:/src/antiphon/server");
        git.Calls.Single().Arguments.ShouldBe(["rev-parse", "--show-toplevel"]);

        var files = new MockFileSystem();
        var sibling = files.Path.Combine(files.Path.GetTempPath(), "work", "antiphon-orchestrator");
        var checkout = files.Path.Combine(files.Path.GetTempPath(), "work", "antiphon");
        files.AddDirectory(checkout);
        files.AddFile(files.Path.Combine(sibling, "antiphon.workspace.json"), new MockFileData("""{"checkout":"../antiphon"}"""));
        var marked = new CardApiFake();
        CardCommandRun.Run(marked.Http.Transport, ["get", "CARD-0051", "--json"], files: files, workingDirectory: sibling).Exit.ShouldBe(0);
        marked.Query("GET /api/cards/CARD-0051", "cwd").ShouldBe(checkout);

        var plain = new CardApiFake();
        CardCommandRun.Run(plain.Http.Transport, ["get", "CARD-0051", "--json"], files: files, workingDirectory: checkout).Exit.ShouldBe(0);
        plain.Query("GET /api/cards/CARD-0051", "cwd").ShouldBe(checkout);
        plain.Query("GET /api/cards/CARD-0051", "boardId").ShouldBeNull();
    }

    [Test]
    public void Board_names_resolve_ignoring_case_and_a_guid_is_sent_as_given()
    {
        var api = new CardApiFake();
        CardCommandRun.Run(api.Http.Transport, ["get", "CARD-0051", "--board", "gym stat", "--json"]).Exit.ShouldBe(0);
        api.Requests.ShouldBe(["GET /api/boards", "GET /api/cards/CARD-0051"]);
        api.Query("GET /api/cards/CARD-0051", "boardId").ShouldBe("b2");

        var guid = new CardApiFake();
        CardCommandRun.Run(guid.Http.Transport, ["get", "#51", "--board", BoardId.ToUpperInvariant(), "--json"]).Exit.ShouldBe(0);
        guid.Requests.ShouldBe(["GET /api/cards/#51"]);
        guid.Query("GET /api/cards/#51", "boardId").ShouldBe(BoardId.ToUpperInvariant());
        guid.Http.Calls.Single().Uri.ShouldContain("/api/cards/%2351?");

        var unknown = CardCommandRun.Run(new CardApiFake().Http.Transport, ["get", "CARD-0051", "--board", "Nope"]);
        unknown.Exit.ShouldBe(1);
        unknown.Error.Trim().ShouldBe("No board named 'Nope'. Known boards: Antiphon, Gym Stat, Twin, twin");
        var ambiguous = CardCommandRun.Run(new CardApiFake().Http.Transport, ["get", "CARD-0051", "--board", "TWIN"]);
        ambiguous.Exit.ShouldBe(1);
        ambiguous.Error.Trim().ShouldBe("'TWIN' names 2 boards - pass the guid instead.");
    }

    [Test]
    public void Move_names_the_columns_resolves_a_state_key_and_says_whether_an_agent_started()
    {
        var missing = new CardApiFake();
        var noTarget = CardCommandRun.Run(missing.Http.Transport, ["move", "CARD-0051"]);
        noTarget.Exit.ShouldBe(1);
        noTarget.Error.Trim().ShouldBe("A --to column is required for move. This board has: Backlog, In Progress, Done");
        var nowhere = CardCommandRun.Run(missing.Http.Transport, ["move", "CARD-0051", "--to", "Nowhere"]);
        nowhere.Error.Trim().ShouldBe("No column 'Nowhere' on this board. It has: Backlog, In Progress, Done");
        missing.Requests.ShouldNotContain(r => r.StartsWith("PATCH", StringComparison.Ordinal));

        var suppressed = new CardApiFake();
        suppressed.Answers["PATCH /api/cards/" + CardId] = $$"""{"card":{{suppressed.Card.ToJsonString()}},"spawnSuppressed":true}""";
        var quiet = CardCommandRun.Run(suppressed.Http.Transport, ["move", "CARD-0051", "--to", "inprogress"]);
        quiet.Exit.ShouldBe(0, quiet.All);
        suppressed.Body("PATCH /api/cards/" + CardId).ToJsonString().ShouldBe($$"""{"boardColumnId":"c2","concurrencyToken":"{{Token}}"}""");
        Lines(quiet.Output).ShouldBe(
        [
            "CARD-0051  Backlog  Normal/Normal  rank 0 (auto)  Fix the thing",
            "moved to    In Progress",
            "moved into an active column; NO agent was started - the tick will not pick it up either; re-run with --spawn (or POST /spawn) to start one",
        ]);

        var spawned = new CardApiFake();
        spawned.Answers["PATCH /api/cards/" + CardId] = $$"""{"card":{{spawned.Card.ToJsonString()}},"spawnedSessionId":"sess-9"}""";
        var started = CardCommandRun.Run(spawned.Http.Transport, ["move", "CARD-0051", "--to", "In Progress", "--spawn", "--reason", "go"]);
        started.Exit.ShouldBe(0, started.All);
        spawned.Body("PATCH /api/cards/" + CardId).ToJsonString().ShouldBe($$"""{"boardColumnId":"c2","concurrencyToken":"{{Token}}","reason":"go","spawn":true}""");
        Lines(started.Output).Last().ShouldBe("started     session sess-9");
    }

    [Test]
    public void Reopen_archive_and_unarchive_send_their_bodies_and_print_their_lines()
    {
        var reopen = new CardApiFake();
        reopen.Answers["POST /api/cards/" + CardId + "/reopen"] =
            $$$"""{"card":{{{reopen.Card.ToJsonString()}}},"trackerPush":{"trackerKind":"GitHubIssues","outcome":"Reopened","externalKey":"#12","url":"https://example.test/12"}}""";
        var aimed = CardCommandRun.Run(reopen.Http.Transport, ["reopen", "CARD-0051", "--reason", "back", "--to", "backlog", "--by", "op"]);
        aimed.Exit.ShouldBe(0, aimed.All);
        reopen.Body("POST /api/cards/" + CardId + "/reopen").ToJsonString().ShouldBe($$"""{"concurrencyToken":"{{Token}}","reason":"back","boardColumnId":"c1","reopenedBy":"op"}""");
        Lines(aimed.Output).Skip(1).ShouldBe(["GitHub      reopened #12 (https://example.test/12)", "reopened to Backlog"]);
        var server = CardCommandRun.Run(reopen.Http.Transport, ["reopen", "CARD-0051", "--reason", "back"]);
        Lines(server.Output).Last().ShouldBe("reopened to Backlog", "no --to: the server's column, read from the answer");

        var archive = new CardApiFake();
        var archived = CardCommandRun.Run(archive.Http.Transport, ["archive", "CARD-0051", "--reason", "gone", "--by", "op"]);
        archived.Exit.ShouldBe(0, archived.All);
        archive.Body("POST /api/cards/" + CardId + "/archive").ToJsonString().ShouldBe($$"""{"concurrencyToken":"{{Token}}","reason":"gone","archivedBy":"op"}""");
        Lines(archived.Output).Last().ShouldBe("archived (reversible: scripts/antiphon card unarchive)");
        var restored = CardCommandRun.Run(archive.Http.Transport, ["unarchive", "CARD-0051", "--reason", "wanted", "--by", "op"]);
        archive.Body("POST /api/cards/" + CardId + "/unarchive").ToJsonString().ShouldBe($$"""{"concurrencyToken":"{{Token}}","reason":"wanted","unarchivedBy":"op"}""");
        Lines(restored.Output).Last().ShouldBe("back on the board");
    }

    [Test]
    public void Reorder_and_order_send_their_bodies_and_print_positions_and_skips()
    {
        var api = new CardApiFake();
        api.Answers["PATCH /api/cards/" + CardId + "/position"] = """{"identifier":"CARD-0051","status":"Backlog","importance":"Normal","urgency":"Normal","rank":0,"position":1,"title":"Fix the thing"}""";
        var before = CardCommandRun.Run(api.Http.Transport, ["reorder", "CARD-0051", "--before", " CARD-0050 ", "--reason", "first", "--by", "op"]);
        before.Exit.ShouldBe(0, before.All);
        api.Body("PATCH /api/cards/" + CardId + "/position").ToJsonString().ShouldBe($$"""{"concurrencyToken":"{{Token}}","before":"CARD-0050","reason":"first","editedBy":"op"}""");
        Lines(before.Output).ShouldBe(["CARD-0051  Backlog  Normal/Normal  rank 0 pos 1 (auto)  Fix the thing", "pos         1"]);
        CardCommandRun.Run(api.Http.Transport, ["reorder", "CARD-0051", "--bottom"]).Exit.ShouldBe(0);
        api.Body("PATCH /api/cards/" + CardId + "/position").ToJsonString().ShouldBe($$"""{"concurrencyToken":"{{Token}}","placement":"Bottom"}""");

        var files = new MockFileSystem();
        var directory = files.Path.Combine(files.Path.GetTempPath(), "orders");
        files.AddFile(files.Path.Combine(directory, "order.txt"), new MockFileData("CARD-0002\n\n  CARD-0001 High  \nCARD-0003 Low Soon extra words\n"));
        api.Answers["POST /api/boards/" + BoardId + "/card-order"] =
            """{"cards":[{"identifier":"CARD-0002","status":"Backlog","importance":"Normal","urgency":"Normal","rank":0,"position":1,"title":"Second"}],"skippedHumanRated":[{"identifier":"CARD-0009","importance":"Critical"}]}""";
        var order = CardCommandRun.Run(api.Http.Transport, ["order", "--board", BoardId, "--order-file", "order.txt", "--reason", "second first", "--override-human-ratings"],
            files: files, workingDirectory: directory);
        order.Exit.ShouldBe(0, order.All);
        api.Body("POST /api/boards/" + BoardId + "/card-order").ToJsonString().ShouldBe(
            """{"cards":[{"id":"CARD-0002"},{"id":"CARD-0001","importance":"High"},{"id":"CARD-0003","importance":"Low","urgency":"Soon extra words"}],"reason":"second first","overrideHumanRatings":true}""");
        Lines(order.Output).ShouldBe(["CARD-0002  Backlog  Normal/Normal  rank 0 pos 1 (auto)  Second", "skipped     CARD-0009  human-rated Critical"]);

        var missing = CardCommandRun.Run(api.Http.Transport, ["order", "--board", BoardId, "--order-file", "missing.txt", "--reason", "r"], files: files, workingDirectory: directory);
        missing.Exit.ShouldBe(1);
        missing.Error.Trim().ShouldBe("Order file not found: missing.txt");
    }

    [Test]
    public void Edit_refuses_nothing_to_change_and_clears_the_alias_and_due_time()
    {
        var api = new CardApiFake();
        var nothing = CardCommandRun.Run(api.Http.Transport, ["edit", "CARD-0051", "--reason", "r"]);
        nothing.Exit.ShouldBe(1);
        nothing.Error.Trim().ShouldStartWith("Nothing to change. Pass at least one of --title,");
        api.Requests.ShouldBe(["GET /api/cards/CARD-0051", "GET /api/cards/limits"]);

        var cleared = CardCommandRun.Run(api.Http.Transport, ["edit", "CARD-0051", "--reason", "r", "--clear-alias", "--due-at", "2026-01-01", "--clear-due-at", "--labels", "a, b"]);
        cleared.Exit.ShouldBe(0, cleared.All);
        api.Body("PATCH /api/cards/" + CardId + "/content").ToJsonString().ShouldBe($$"""{"concurrencyToken":"{{Token}}","reason":"r","clearDueAt":true,"labels":["a","b"],"alias":""}""");
        Lines(cleared.Output)[1].ShouldBe("revision    5");
    }

    [Test]
    public void Over_long_text_and_a_bad_alias_fail_locally_naming_the_ceiling()
    {
        var api = new CardApiFake();
        var reason = CardCommandRun.Run(api.Http.Transport, ["edit", "CARD-0051", "--reason", new string('r', 4001), "--title", "never sent"]);
        reason.Exit.ShouldBe(1);
        reason.Error.Trim().ShouldBe("Reason is 4001 characters; the limit is 4000. Trim it, or split the detail across a comment.");
        foreach (var (alias, refusal) in new[]
                 {
                     ("one two three four five six", "Alias must be at most 5 words; got 6."),
                     ("one\ntwo", "Alias must be a single line."),
                     (new string('a', 65), "Alias is 65 characters; the limit is 64. Trim it, or split the detail across a comment."),
                 })
        {
            var run = CardCommandRun.Run(api.Http.Transport, ["new", "--board", BoardId, "--title", "t", "--alias", alias]);
            run.Exit.ShouldBe(1, alias);
            run.Error.Trim().ShouldBe(refusal);
        }
        var file = CardCommandRun.Run(api.Http.Transport, ["new", "--board", BoardId, "--title", "t", "--description-file", "no-such-description.md"]);
        file.Exit.ShouldBe(1);
        file.Error.Trim().ShouldBe("--description-file 'no-such-description.md' does not exist.");
        api.Requests.ShouldNotContain(r => r.StartsWith("PATCH", StringComparison.Ordinal) || r.StartsWith("POST", StringComparison.Ordinal));
    }

    [Test]
    public void A_malformed_command_line_is_a_usage_error()
    {
        foreach (var (args, message) in new (string[], string)[]
                 {
                     (["move", "CARD-1", "--to", "Done", "--json"], "--json does not apply to move"),
                     (["get"], "which card? Pass it after the verb: scripts/antiphon card get CARD-0051 ..."),
                     (["new", "CARD-1", "--board", "b", "--title", "t"], "new takes no card argument."),
                     (["close", "CARD-1"], "A --reason (or --reason-file) is required for 'close'. Say why, not what."),
                     (["edit", "CARD-1", "--reason", "r", "--reason-file", "f"], "Pass --reason or --reason-file, not both."),
                     (["new", "--board", "b", "--title", "t", "--description", "d", "--description-file", "f"], "Pass --description or --description-file, not both."),
                     (["reorder", "CARD-1", "--top", "--bottom"], "Pass only one of --top or --bottom."),
                     (["reorder", "CARD-1", "--top", "--after", "CARD-2"], "--top / --bottom cannot be combined with --before / --after."),
                     (["reorder", "CARD-1"], "reorder needs --before, --after, --top or --bottom."),
                     (["order", "--order-file", "f", "--reason", "r"], "order needs --board <name|guid>."),
                     (["block", "CARD-1"], "Pass --blocked-by <card> for 'block'."),
                     (["edit", "CARD-1", "--reason", "r", "--alias", "x", "--clear-alias"], "Pass --alias or --clear-alias, not both."),
                     (["get", "CARD-1", "--clear-private-notes"], "Private-note and card-file visibility options apply only to new/edit."),
                     (["new", "--board", "b", "--title", "t", "--clear-private-notes"], "--clear-private-notes applies only to edit and cannot be combined with --private-notes-file."),
                     (["unblock", "CARD-1", "--blocked-by", "CARD-2", "--related-board", "x"], "--related-board applies to block."),
                     (["limits", "--board", "x"], "--board does not apply to limits"),
                     (["new", "--board", "b", "--title", "t", "--priority", "2"],
                         "--priority is gone. Use --importance (Low|Normal|High|Critical), --urgency (Normal|Soon|Now), and --due-at / --clear-due-at."),
                     (["new", "--board", "b", "--title", "t", "--urgency", "Later"], "--urgency must be one of Normal, Soon, Now, not 'Later'"),
                 })
            Should.Throw<NightlyUsageException>(() => CardCliOptions.Parse(args, null, null, null)).Message.ShouldBe(message, string.Join(" ", args));
        Should.Throw<NightlyUsageException>(() => CardCliOptions.Parse(["promote", "CARD-1"], null, null, null)).Message.ShouldStartWith("the first argument is the verb (list, search, get,");
        var parsed = CardCliOptions.Parse(["EDIT", "card-51", "--reason", "r", "--importance", "high", "--labels", "a,b", "--alias", ""], "http://api/", "token", "/work");
        (parsed.Verb, parsed.Card, parsed.Importance, parsed.Alias, parsed.Api, parsed.TaskToken, parsed.WorkingDirectory)
            .ShouldBe(("edit", "card-51", "High", "", "http://api", "token", "/work"));
        parsed.Labels.ShouldBe(["a", "b"]);
    }

    // The output's lines, line ends normalised (a description's own LF stays a line break).
    private static string[] Lines(string output) => output.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    /// <summary>The card API stand-in: one card, three columns, four boards, the limits; Answers overrides a "METHOD /path" route.</summary>
    private sealed class CardApiFake
    {
        public CardApiFake() => Http = new RecordingOperatorHttp(Answer);

        public RecordingOperatorHttp Http { get; }

        public JsonObject Card { get; init; } = new()
        {
            ["id"] = CardId, ["identifier"] = "CARD-0051", ["status"] = "Backlog", ["title"] = "Fix the thing", ["importance"] = "Normal",
            ["urgency"] = "Normal", ["rank"] = 0, ["boardId"] = BoardId, ["concurrencyToken"] = Token, ["revisionCount"] = 5,
        };

        public Dictionary<string, string> Answers { get; } = new(StringComparer.Ordinal);

        /// <summary>Each request as "METHOD /path" (no query), in order.</summary>
        public IReadOnlyList<string> Requests => Http.Calls.Select(Route).ToArray();

        /// <summary>The last body sent to "METHOD /path".</summary>
        public JsonNode Body(string route) => JsonNode.Parse(Http.Calls.Last(c => Route(c) == route).Body!)!;

        /// <summary>A query value of the last request to "METHOD /path"; null when it has none.</summary>
        public string? Query(string route, string key)
        {
            var uri = Http.Calls.Last(c => Route(c) == route).Uri;
            var question = uri.IndexOf('?');
            if (question < 0) return null;
            foreach (var pair in uri[(question + 1)..].Split('&'))
            {
                var parts = pair.Split('=', 2);
                if (parts[0] == key) return Uri.UnescapeDataString(parts.Length == 2 ? parts[1] : "");
            }
            return null;
        }

        private static string Route(RecordingOperatorHttp.Call call) => call.Method + " " + Uri.UnescapeDataString(new Uri(call.Uri).AbsolutePath);

        private NightlyReportHttpResponse Answer(RecordingOperatorHttp.Call call)
        {
            var route = Route(call);
            if (Answers.TryGetValue(route, out var answer)) return new(200, answer);
            return route switch
            {
                "GET /api/cards/limits" => new(200, """{"maxTitleLength":300,"maxDescriptionLength":20000,"maxPrivateNotesLength":20000,"maxReasonLength":4000,"maxActorLength":200,"maxAliasLength":64,"maxAliasWords":5,"importanceValues":["Low","Normal","High","Critical"],"urgencyValues":["Normal","Soon","Now"]}"""),
                "GET /api/boards" => new(200, $$"""[{"id":"{{BoardId}}","name":"Antiphon"},{"id":"b2","name":"Gym Stat"},{"id":"t1","name":"Twin"},{"id":"t2","name":"twin"}]"""),
                "GET /api/boards/" + BoardId + "/columns" => new(200, """
                    [{"id":"c1","name":"Backlog","stateKey":"Backlog","cardStatus":"Backlog","isTerminal":false,"columnOrder":0},
                     {"id":"c2","name":"In Progress","stateKey":"InProgress","cardStatus":"InProgress","isTerminal":false,"columnOrder":1},
                     {"id":"c3","name":"Done","stateKey":"Done","cardStatus":"Done","isTerminal":true,"columnOrder":2}]
                    """),
                "GET /api/cards/" + CardId + "/relationships" => new(200, """{"blockedBy":[{"relatedIdentifier":"CARD-0049","relatedStatus":"InProgress","resolved":false}],"blocks":[{"relatedIdentifier":"CARD-0052"}]}"""),
                _ when call.Method == "GET" && route.StartsWith("GET /api/cards/", StringComparison.Ordinal) => new(200, Card.ToJsonString()),
                _ when call.Method == "POST" && route.EndsWith("/reopen", StringComparison.Ordinal) => new(200, $$"""{"card":{{Card.ToJsonString()}}}"""),
                _ when call.Method is "POST" or "PATCH" => new(200, Card.ToJsonString()),
                _ => new(404, """{"detail":"Unexpected request"}"""),
            };
        }
    }
}
