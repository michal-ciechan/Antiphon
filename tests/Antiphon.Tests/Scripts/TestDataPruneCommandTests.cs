using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0217 S9: the prune-test-data command (Antiphon.Nightly.TestDataPrune, which replaced scripts/prune-test-data.ps1 in unit
/// 35 of the 2026-11-22 plan) in process on a recording transport: the dry run is the default and writes nothing, --execute
/// archives (never deletes) only name-matched rows with no agent, live session or open task attached, the open-task guard reads
/// the agent-tasks envelope (CARD-0515), and a failed read archives nothing. Nothing leaves the process.
/// </summary>
[Category("Unit")]
public sealed class TestDataPruneCommandTests
{
    private const string Api = "http://127.0.0.1:17202/";

    [Test]
    public void The_dry_run_is_the_default_and_names_every_guard_without_writing()
    {
        var http = Board();
        var run = Run(http, "task-secret");

        run.Exit.ShouldBe(0, run.Error);
        http.Calls.ShouldAllBe(call => call.Method == "GET");
        http.Calls.Select(call => call.Path).ShouldBe(
        [
            "/api/projects?includeArchived=true", "/api/boards?includeArchived=true", "/api/agents", "/api/agent-tasks", "/api/cards/c3",
        ], "the two open tasks on c3 read the card once; a Done task reads none");
        http.Calls.ShouldAllBe(call => call.Headers["X-Antiphon-Task-Token"] == "task-secret");
        (run.Output + run.Error).ShouldNotContain("task-secret");
        var lines = run.Output.ReplaceLineEndings("\n").Split('\n');
        lines[0].ShouldBe("DRY RUN - listing what would be archived; nothing was written");
        lines[1].ShouldBe("API http://127.0.0.1:17202");
        lines[2].ShouldBe("Patterns: " + string.Join(", ", TestDataPruneOptions.DefaultPatterns));
        lines[3].ShouldBe("Projects: 1 qualify, 2 skipped of 3 name-matched (4 total)");
        lines[4].ShouldBe("Boards:   2 qualify, 2 skipped of 4 name-matched (5 total)");
        run.Output.ShouldContain("PROJECTS TO ARCHIVE\n  project  card-task-alpha  p1  matched ^card-task-; agents=0 liveSessions=0 openTasks=0", Case.Sensitive);
        run.Output.ShouldContain("  project  Catalog Test 2  p2  matched ^Catalog Test; skip=already archived");
        run.Output.ShouldContain("  project  TUI Probe  p4  matched ^TUI Probe$; skip=agent attached (agent-x)");
        run.Output.ShouldContain("BOARDS TO ARCHIVE\n  board  task-0123abcd  b1  matched ^task-[0-9a-fA-F]{8}$; agents=0 liveSessions=0 openTasks=0");
        run.Output.ShouldContain("  board  CARD0042-repro  b2  matched ^card\\d+-; agents=0 liveSessions=0 openTasks=0", Case.Sensitive, "-match ignores case");
        run.Output.ShouldContain("  board  CARD-12 Verify thing  b3  matched ^CARD-\\d+ Verify ; skip=non-terminal task attached");
        run.Output.ShouldContain("  board  Card0007 Board  b4  matched ^Card0007 ; skip=agent attached (agent-x)", Case.Sensitive);
        run.Output.ShouldNotContain("Real Project");
        run.Output.ShouldNotContain("Real Board");
        run.Output.ShouldNotContain("Archived:");
    }

    [Test]
    public void Execute_archives_boards_then_projects_and_lists_a_failed_archive()
    {
        var http = Board(call => call.Path == "/api/boards/b2/archive" ? new(500, "boom") : null);
        var run = Run(http, null, "--execute", "--reason", "Bulk cleanup");

        run.Exit.ShouldBe(1, "one archive failed");
        var writes = http.Calls.Where(call => call.Method != "GET").ToList();
        writes.Select(call => call.Method + " " + call.Path).ShouldBe(
            ["POST /api/boards/b1/archive", "POST /api/boards/b2/archive", "POST /api/projects/p1/archive"], "boards first; the rest still run after a failure");
        writes.ShouldAllBe(call => call.Body == """{"reason":"Bulk cleanup","archivedBy":"prune-test-data"}""");
        http.Calls.ShouldAllBe(call => !call.Headers.ContainsKey("X-Antiphon-Task-Token"));
        run.Output.ShouldStartWith("EXECUTE - archiving qualifying rows (never deleting)");
        run.Output.ShouldContain("Archived: 2; errors: 1");
        run.Output.ShouldContain("  FAIL board CARD0042-repro  Antiphon POST /api/boards/b2/archive failed: HTTP 500 boom");

        var defaults = Board();
        Run(defaults, null, "--execute").Exit.ShouldBe(0);
        defaults.Calls.First(call => call.Method == "POST").Body.ShouldBe("""{"reason":"Test residue (prune-test-data)","archivedBy":"prune-test-data"}""");
    }

    [Test]
    public void Json_prints_one_object_with_the_rows_and_the_mutations()
    {
        var dry = Run(Board(), null, "--json");
        dry.Exit.ShouldBe(0, dry.Error);
        var result = JsonNode.Parse(dry.Output)!.AsObject();
        result["Mode"]!.GetValue<string>().ShouldBe("dry-run");
        result["TouchedNothing"]!.GetValue<bool>().ShouldBeTrue();
        result["ProjectQualify"]!.GetValue<int>().ShouldBe(1);
        result["ProjectSkip"]!.GetValue<int>().ShouldBe(2);
        result["BoardQualify"]!.GetValue<int>().ShouldBe(2);
        result["BoardSkip"]!.GetValue<int>().ShouldBe(2);
        result["Mutations"]!.AsArray().Count.ShouldBe(0);
        var b4 = result["Boards"]!.AsArray().Single(row => row!["Id"]!.GetValue<string>() == "b4")!;
        b4["Qualify"]!.GetValue<bool>().ShouldBeFalse();
        b4["Skip"]!.GetValue<string>().ShouldBe("agent attached (agent-x)");
        b4["Agents"]!.GetValue<int>().ShouldBe(1);
        result["Projects"]!.AsArray().Single(row => row!["Id"]!.GetValue<string>() == "p2")!["Archived"]!.GetValue<bool>().ShouldBeTrue();
        dry.Output.ShouldNotContain("DRY RUN");

        var executed = JsonNode.Parse(Run(Board(), null, "--json", "--execute").Output)!.AsObject();
        executed["Mode"]!.GetValue<string>().ShouldBe("execute");
        executed["TouchedNothing"]!.GetValue<bool>().ShouldBeFalse();
        executed["Mutations"]!.AsArray().Select(row => row!["Kind"]!.GetValue<string>() + " " + row["Id"]!.GetValue<string>() + " " + row["ArchivedAt"]!.GetValue<string>()).ShouldBe(
            ["board b1 2026-11-24T00:00:00Z", "board b2 2026-11-24T00:00:00Z", "project p1 2026-11-24T00:00:00Z"]);
    }

    [Test]
    public void Match_replaces_the_defaults_and_a_bad_pattern_is_a_usage_error()
    {
        var http = Board();
        var run = Run(http, null, "--match", "^real (project|board)$", "--match", "^x{1,3}$");
        run.Exit.ShouldBe(0, run.Error);
        run.Output.ShouldContain("Patterns: ^real (project|board)$, ^x{1,3}$");
        run.Output.ShouldContain("Projects: 0 qualify, 1 skipped of 1 name-matched (4 total)");
        run.Output.ShouldContain("  project  Real Project  p3  matched ^real (project|board)$; skip=non-terminal task attached");
        run.Output.ShouldContain("  board  Real Board  b5  matched ^real (project|board)$; agents=0 liveSessions=0 openTasks=0");
        run.Output.ShouldNotContain("card-task-alpha");

        Should.Throw<NightlyUsageException>(() => TestDataPruneOptions.Parse(["--match", "["], Api, null)).Message.ShouldContain("not a valid regular expression");
        Should.Throw<NightlyUsageException>(() => TestDataPruneOptions.Parse(["--match"], Api, null)).Message.ShouldContain("--match needs a value");
        Should.Throw<NightlyUsageException>(() => TestDataPruneOptions.Parse(["--match", " "], Api, null));
        Should.Throw<NightlyUsageException>(() => TestDataPruneOptions.Parse(["--reason", ""], Api, null));
        Should.Throw<NightlyUsageException>(() => TestDataPruneOptions.Parse(["--delete"], Api, null));
        Should.Throw<NightlyUsageException>(() => TestDataPruneOptions.Parse(["--execute", "--execute"], Api, null));
    }

    [Test]
    public void A_failed_read_archives_nothing()
    {
        foreach (var failing in new[] { "/api/boards?includeArchived=true", "/api/agent-tasks", "/api/cards/c3" })
        {
            var http = Board(call => call.Path == failing ? new(503, "down") : null);
            var run = Run(http, null, "--execute");
            run.Exit.ShouldBe(1);
            run.Error.Trim().ShouldBe("Antiphon GET " + failing + " failed: HTTP 503 down");
            http.Calls.ShouldAllBe(call => call.Method == "GET", "no archive after a failed read");
            run.Output.ShouldBeEmpty();
        }
        var lost = new RecordingOperatorHttp(_ => throw new System.Net.Http.HttpRequestException("connection refused"));
        var unreachable = Run(lost, null);
        unreachable.Exit.ShouldBe(1);
        unreachable.Error.ShouldContain("Antiphon GET /api/projects?includeArchived=true failed: connection refused");
    }

    // Four projects and five boards: p1/b1 and b2 qualify, p2 is archived, p4 and b4 carry agent-x, b3 holds two open tasks on
    // card c3, and p3, b5 match no default pattern. override answers a call first (null: the board's answer).
    private static RecordingOperatorHttp Board(Func<RecordingOperatorHttp.Call, NightlyReportHttpResponse?>? @override = null) => new(call =>
    {
        if (@override?.Invoke(call) is { } answer) return answer;
        return call.Path switch
        {
            "/api/projects?includeArchived=true" => new(200, """
                [ { "id": "p1", "name": "card-task-alpha", "archivedAt": null },
                  { "id": "p2", "name": "Catalog Test 2", "archivedAt": "2026-11-01T00:00:00Z" },
                  { "id": "p3", "name": "Real Project" },
                  { "id": "p4", "name": "TUI Probe" } ]
                """),
            "/api/boards?includeArchived=true" => new(200, """
                [ { "id": "b1", "name": "task-0123abcd", "projectId": "p1" },
                  { "id": "b2", "name": "CARD0042-repro", "projectId": "p3" },
                  { "id": "b3", "name": "CARD-12 Verify thing", "projectId": "p3" },
                  { "id": "b4", "name": "Card0007 Board", "projectId": "p4" },
                  { "id": "b5", "name": "Real Board", "projectId": "p3" } ]
                """),
            "/api/agents" => new(200, """
                [ { "name": "agent-x", "boardId": "b4", "liveSession": null },
                  { "name": "idle", "boardId": null } ]
                """),
            "/api/agent-tasks" => new(200, """
                { "scope": null,
                  "items": [ { "status": "Working", "cardId": "c3" }, { "status": "queued", "cardId": "c3" }, { "status": "Done", "cardId": "c9" } ],
                  "excluded": { "count": 0 } }
                """),
            "/api/cards/c3" => new(200, """{ "id": "c3", "boardId": "b3" }"""),
            _ when call.Method == "POST" => new(200, """{ "archivedAt": "2026-11-24T00:00:00Z" }"""),
            _ => new(404, "not found"),
        };
    });

    private static (int Exit, string Output, string Error) Run(RecordingOperatorHttp http, string? taskToken, params string[] args)
    {
        var output = new StringWriter { NewLine = "\n" };
        var error = new StringWriter { NewLine = "\n" };
        var host = new NightlyHost
        {
            Clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 24, 0, 0, 0, TimeSpan.Zero)),
            Git = new FakeGit().Run,
            Launcher = new RecordingLauncher(),
            Processes = new FakeProcessTable(),
            Probe = new FakeHostProbe(),
            Out = output,
            Error = error,
            Trace = TextWriter.Null,
        };
        var exit = TestDataPrune.Run(TestDataPruneOptions.Parse(args, Api, taskToken), host, http.Transport);
        return (exit, output.ToString(), error.ToString());
    }
}
