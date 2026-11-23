using System.Text;
using System.Text.Json;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The project command's new, readiness and catalog verbs and its project lookup (Antiphon.Nightly.ProjectCli, which replaced
/// scripts/project.ps1 in unit 24 of the 2026-11-22 plan) in process, on a recording transport: the setup body with its
/// agent options, the profile resolved through the catalog, the prompt file sent as is, the setup notes printed loudly, the
/// readiness table with the missing required checks first, and the refusals. The set verb is ProjectScriptWorkspaceTests
/// and CommitOnSettleScriptTests. Nothing leaves the process.
/// </summary>
[Category("Integration")]
public sealed class ProjectCommandTests
{
    private const string ProjectId = "11111111-1111-1111-1111-111111111111";
    private const string ProfileId = "88888888-8888-8888-8888-888888888888";

    private const string CatalogJson = $$$"""
        {"modelLevels":[{"key":"Frontier","aliasesByKind":{"ClaudeCode":"fable","Codex":"gpt-6.2-astra"},"blurb":"hardest"}],
         "replyStyles":[{"key":"Terse","description":"short"}],
         "bundles":[{"key":"board-api","summary":"the board"}],
         "presets":[{"key":"orchestrator","description":"runs the board","modelLevel":"Frontier","replyStyle":"Terse","alwaysOn":true,
                     "bundleKeys":["orchestrator","board-api"],"namePattern":"{project} orchestrator","systemPromptTemplate":"lead"}],
         "profiles":[{"displayName":"Claude Max","kind":"ClaudeCode","id":"{{{ProfileId}}}"},{"displayName":"Twin","kind":"Codex","id":"a"},{"displayName":"twin","kind":"Codex","id":"b"}],
         "delegation":{"allowedRoots":["C:\\src","D:\\work"],"allowedRootsIsEmpty":false,"maxConcurrentTasks":6,"maxCostUsdPerRoot":25.5,"maxDepth":3,"defaultLevel":"High"}}
        """;

    private const string ReadinessJson = """
        {"canDispatch":false,"checks":[
          {"key":"git","level":"Recommended","status":"Ok","summary":"repository found","fixes":[]},
          {"key":"runner","level":"Required","status":"Missing","summary":"no runner","fixes":[{"label":"Start a runner","route":"/settings/runners"},{"label":"Read the doc","route":null}]},
          {"key":"agent","level":"Required","status":"Ok","summary":"orchestrator","fix":{"label":"Open","route":"/agents"}}]}
        """;

    [Test]
    public async Task New_sends_the_setup_body_with_the_agent_options_and_prints_the_notes_loudly()
    {
        File.ReadAllBytes(Path.Combine(Antiphon.Tests.Application.DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "ProjectCli.cs")).ShouldAllBe(b => b < 128);
        var prompt = "Line one with \"quotes\" and $(sub)\nline two\n";
        var file = Path.Combine(Path.GetTempPath(), "project-prompt-" + Guid.NewGuid().ToString("N") + ".md");
        await File.WriteAllTextAsync(file, prompt, new UTF8Encoding(false));
        try
        {
            var http = new RecordingOperatorHttp(call => call.Path == "/api/projects/setup-catalog" ? new(200, CatalogJson) : new(200, $$"""
                {"project":{"id":"{{ProjectId}}","name":"demo"},"board":{"id":"b1","name":"Demo"},"agent":{"id":"a1","name":"demo orchestrator"},
                 "readiness":{"canDispatch":true,"checks":[]},"notes":["Agent start refused: no runner.","Start it later."]}
                """));
            var run = Run(http, "new", "--dir", @"C:\src\demo", "--create-directory", "--name", "demo", "--git-url", "https://example.test/demo.git",
                "--base-branch", "main", "--board-name", "Demo", "--orchestrator", "--agent-name", "demo orchestrator", "--profile", " claude max ",
                "--level", "frontier", "--reply-style", "terse", "--bundles", "board-api, orchestrator", "--prompt-file", file, "--remote-control", "--start");

            run.Exit.ShouldBe(0, run.All);
            http.Calls.Select(c => c.Method + " " + c.Path).ShouldBe(["GET /api/projects/setup-catalog", "POST /api/projects/setup"]);
            using var body = JsonDocument.Parse(http.Calls[1].Body!);
            var root = body.RootElement;
            root.GetProperty("directory").GetString().ShouldBe(@"C:\src\demo");
            root.GetProperty("createDirectory").GetBoolean().ShouldBeTrue();
            root.GetProperty("name").GetString().ShouldBe("demo");
            root.GetProperty("gitRepositoryUrl").GetString().ShouldBe("https://example.test/demo.git");
            root.GetProperty("baseBranch").GetString().ShouldBe("main");
            root.GetProperty("boardName").GetString().ShouldBe("Demo");
            root.GetProperty("startAgent").GetBoolean().ShouldBeTrue();
            var agent = root.GetProperty("agent");
            agent.GetProperty("preset").GetString().ShouldBe("orchestrator");
            agent.GetProperty("name").GetString().ShouldBe("demo orchestrator");
            agent.GetProperty("tuiProfileId").GetString().ShouldBe(ProfileId);
            agent.GetProperty("modelLevel").GetString().ShouldBe("Frontier");
            agent.GetProperty("replyStyle").GetString().ShouldBe("Terse");
            agent.GetProperty("bundleKeys").EnumerateArray().Select(k => k.GetString()).ShouldBe(["board-api", "orchestrator"]);
            agent.GetProperty("systemPromptAppend").GetString().ShouldBe(prompt);
            agent.GetProperty("remoteControlEnabled").GetBoolean().ShouldBeTrue();
            run.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Take(4).ShouldBe(
                ["Project: demo (" + ProjectId + ")", "Board:   Demo (b1)", "Agent:   demo orchestrator (a1)", "Ready to dispatch"]);
            run.Error.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ShouldBe(
                ["WARNING: SETUP NOTES - AGENT START MAY HAVE BEEN REFUSED:", "WARNING:   Agent start refused: no runner.", "WARNING:   Start it later."]);
        }
        finally { File.Delete(file); }
    }

    [Test]
    public void New_without_an_agent_sends_null_and_a_bare_preset_sends_only_the_preset()
    {
        var http = new RecordingOperatorHttp(_ => new(200, """{"project":{"id":"p","name":"n"},"board":{"id":"b","name":"B"},"agent":null,"readiness":null,"notes":[]}"""));
        var none = Run(http, "new", "--dir", "/srv/demo", "--no-agent");
        none.Exit.ShouldBe(0, none.All);
        http.Calls[^1].Body.ShouldBe("""{"directory":"/srv/demo","agent":null}""");
        none.Output.ShouldContain("Agent:   none");
        none.Error.ShouldBeEmpty();

        Run(http, "new", "--dir", "/srv/demo", "--worker").Exit.ShouldBe(0);
        http.Calls[^1].Body.ShouldBe("""{"directory":"/srv/demo","agent":{"preset":"worker"}}""");
        Run(http, "new", "--dir", "/srv/demo").Exit.ShouldBe(0);
        http.Calls[^1].Body.ShouldBe("""{"directory":"/srv/demo"}""", "no preset and no agent option: the server's default");
        Run(http, "new", "--dir", "/srv/demo", "--level", "Low").Exit.ShouldBe(0);
        http.Calls[^1].Body.ShouldBe("""{"directory":"/srv/demo","agent":{"preset":null,"modelLevel":"Low"}}""");
    }

    [Test]
    public void New_refuses_an_unknown_or_ambiguous_profile_and_a_missing_prompt_file_before_the_setup()
    {
        var http = new RecordingOperatorHttp(_ => new(200, CatalogJson));
        var unknown = Run(http, "new", "--dir", "d", "--profile", "Nobody");
        unknown.Exit.ShouldBe(1);
        unknown.Error.Trim().ShouldBe("No enabled profile matches 'Nobody'. Run: scripts/antiphon project catalog");
        var ambiguous = Run(http, "new", "--dir", "d", "--profile", "TWIN");
        ambiguous.Error.Trim().ShouldBe("'TWIN' matches 2 enabled profiles. Pass the profile guid instead.");
        Run(http, "new", "--dir", "d", "--profile", ProfileId.ToUpperInvariant(), "--prompt-file", "no-such-prompt.md").Error.Trim()
            .ShouldBe("--prompt-file 'no-such-prompt.md' does not exist.");
        http.Calls.ShouldAllBe(c => c.Path == "/api/projects/setup-catalog");
    }

    [Test]
    public void Readiness_resolves_the_project_and_prints_the_missing_required_checks_first()
    {
        var http = new RecordingOperatorHttp(call => call.Path == "/api/projects"
            ? new(200, $$"""[{"id":"{{ProjectId}}","name":"Antiphon"},{"id":"22222222-2222-2222-2222-222222222222","name":"other"}]""")
            : new(200, ReadinessJson));
        var run = Run(http, "readiness", "antiphon");
        run.Exit.ShouldBe(0, run.All);
        http.Calls.Select(c => c.Path).ShouldBe(["/api/projects", "/api/projects/" + ProjectId + "/readiness"]);
        string Row(string key, string level, string status, string summary, string fix) => key.PadRight(22) + " " + level.PadRight(12) + " " + status.PadRight(15) + " " + summary.PadRight(48) + " " + fix;
        run.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ShouldBe(
        [
            "Project: Antiphon (" + ProjectId + ")",
            "Cannot dispatch yet",
            Row("key", "level", "status", "summary", "fix"),
            Row("runner", "Required", "Missing", "no runner", "Start a runner (/settings/runners); Read the doc"),
            Row("git", "Recommended", "Ok", "repository found", ""),
            Row("agent", "Required", "Ok", "orchestrator", "Open (/agents)"),
        ]);

        var json = Run(http, "readiness", ProjectId.ToUpperInvariant(), "--json");
        json.Exit.ShouldBe(0, json.All);
        JsonDocument.Parse(json.Output).RootElement.GetProperty("canDispatch").GetBoolean().ShouldBeFalse();
    }

    [Test]
    public void An_unresolvable_project_names_the_candidates()
    {
        var http = new RecordingOperatorHttp(_ => new(200, """[{"id":"1","name":"Demo"},{"id":"2","name":"demo"},{"id":"3","name":"other"}]"""));
        var ambiguous = Run(http, "readiness", "DEMO");
        ambiguous.Exit.ShouldBe(1);
        ambiguous.Error.Trim().ShouldBe("Project 'DEMO' is not uniquely resolvable. Candidates: Demo (1), demo (2)");
        var missing = Run(http, "readiness", "nothing");
        missing.Error.Trim().ShouldBe("Project 'nothing' is not uniquely resolvable. Candidates: Demo (1), demo (2), other (3)");
        Run(http, "readiness", "demo").Exit.ShouldBe(0, "an exact name wins over a case-insensitive twin");
    }

    [Test]
    public void Catalog_prints_each_section()
    {
        var http = new RecordingOperatorHttp(_ => new(200, CatalogJson));
        var run = Run(http, "catalog");
        run.Exit.ShouldBe(0, run.All);
        run.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ShouldBe(
        [
            "Model levels:",
            "  Frontier (ClaudeCode=fable, Codex=gpt-6.2-astra): hardest",
            "Reply styles:",
            "  Terse: short",
            "Bundles:",
            "  board-api: the board",
            "Presets:",
            "  orchestrator: runs the board; level=Frontier; style=Terse; alwaysOn=True; bundles=orchestrator, board-api; name={project} orchestrator; prompt=lead",
            "Profiles:",
            "  Claude Max (ClaudeCode) " + ProfileId,
            "  Twin (Codex) a",
            "  twin (Codex) b",
            "Delegation:",
            @"  allowedRoots=C:\src, D:\work; allowedRootsIsEmpty=False; maxConcurrentTasks=6; maxCostUsdPerRoot=25.5; maxDepth=3; defaultLevel=High",
        ]);
    }

    [Test]
    public void A_malformed_command_line_is_a_usage_error()
    {
        Should.Throw<NightlyUsageException>(() => ProjectCliOptions.Parse(["catalog", "antiphon"], null, null)).Message.ShouldBe("catalog takes no project argument");
        Should.Throw<NightlyUsageException>(() => ProjectCliOptions.Parse(["new", "antiphon", "--dir", "d"], null, null)).Message
            .ShouldBe("new does not take a positional project argument; use --dir");
        Should.Throw<NightlyUsageException>(() => ProjectCliOptions.Parse(["new"], null, null)).Message.ShouldBe("new requires --dir <path>");
        Should.Throw<NightlyUsageException>(() => ProjectCliOptions.Parse(["new", "--dir", "d", "--worker", "--no-agent"], null, null)).Message
            .ShouldBe("pass only one of --orchestrator, --worker, or --no-agent");
        Should.Throw<NightlyUsageException>(() => ProjectCliOptions.Parse(["new", "--dir", "d", "--no-agent", "--agent-name", "x"], null, null)).Message
            .ShouldBe("--no-agent cannot be combined with agent options");
        Should.Throw<NightlyUsageException>(() => ProjectCliOptions.Parse(["set", "antiphon"], null, null)).Message.ShouldStartWith("set requires --commit-on-settle");
        Should.Throw<NightlyUsageException>(() => ProjectCliOptions.Parse(["readiness"], null, null)).Message.ShouldStartWith("which project?");
        Should.Throw<NightlyUsageException>(() => ProjectCliOptions.Parse(["readiness", "p", "--dir", "d"], null, null)).Message.ShouldBe("--dir does not apply to readiness");
        Should.Throw<NightlyUsageException>(() => ProjectCliOptions.Parse(["new", "--dir", "d", "--reply-style", "Loud"], null, null)).Message
            .ShouldBe("--reply-style must be one of Normal, Terse, Caveman, Brief, Phone, Explanatory, not 'Loud'");
    }

    private static BoardCommandRun.Result Run(RecordingOperatorHttp http, params string[] args) =>
        BoardCommandRun.Run(host => ProjectCli.Run(ProjectCliOptions.Parse(args, BoardCommandRun.Api, null), host, new System.IO.Abstractions.FileSystem(), http.Transport));
}
