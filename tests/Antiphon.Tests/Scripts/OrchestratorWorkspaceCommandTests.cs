using System.IO.Abstractions.TestingHelpers;
using System.Text;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0251: the orchestrator-workspace command (Antiphon.Nightly.OrchestratorWorkspace, which replaced
/// scripts/orchestrator-workspace.ps1 in unit 26 of the 2026-11-22 plan; the script had no test of its own) in process. The
/// checkout, the sibling and the user profile are on an in-memory file system under a temp-path root, the project API is a
/// recording transport and the junction a recording seam, so nothing on this machine is read or written and nothing leaves
/// the process.
/// </summary>
[Category("Integration")]
public sealed class OrchestratorWorkspaceCommandTests
{
    private const string ProjectId = "aaaaaaaa-1111-2222-3333-444444444444";

    [Test]
    public void Plan_previews_the_sibling_and_writes_nothing()
    {
        var world = new SiblingWorkspaceWorld();
        var run = world.Run("plan", "antiphon");

        run.Exit.ShouldBe(0, run.All);
        Lines(run.Output).ShouldBe([
            "current: Warning - Orchestrator 'orch' runs in the checkout itself",
            "checkout: " + world.Checkout,
            "sibling:  " + world.Sibling,
            "cli:      claude",
            "would write:",
            "  " + Path.Combine(world.Sibling, "antiphon.workspace.json"),
            "  " + Path.Combine(world.Sibling, "CLAUDE.md"),
            "  " + Path.Combine(world.Sibling, ".claude", "settings.json"),
            "  junction " + Path.Combine(world.Sibling, ".claude", "skills") + " -> " + Path.Combine(world.Checkout, ".claude", "skills"),
            "  ~/.claude.json projects[\"" + world.Sibling.Replace('\\', '/') + "\"].hasClaudeMdExternalIncludesApproved = true",
            "would NOT write: the agent WorkingDirectory PATCH (operator / S5).",
            "project: " + ProjectId]);
        world.Requests.ShouldBe(["GET /api/projects", "GET /api/projects/" + ProjectId + "/readiness"]);
        world.Files.Directory.Exists(world.Sibling).ShouldBeFalse("plan never writes");
        world.Files.File.Exists(world.ClaudeJson).ShouldBeFalse();
        world.Junctions.ShouldBeEmpty();
    }

    [Test]
    public void Plan_json_and_the_other_clis()
    {
        var world = new SiblingWorkspaceWorld();
        var json = world.Run("plan", "antiphon", "--json");
        json.Exit.ShouldBe(0, json.All);
        var answer = JsonNode.Parse(json.Output)!;
        answer["checkout"]!.GetValue<string>().ShouldBe(world.Checkout);
        answer["sibling"]!.GetValue<string>().ShouldBe(world.Sibling);
        answer["cli"]!.GetValue<string>().ShouldBe("claude");
        answer["projectId"]!.GetValue<string>().ShouldBe(ProjectId);
        world.Requests.ShouldBe(["GET /api/projects"], "the JSON plan does not read readiness");

        var custom = Path.Combine(world.Root, "elsewhere", "orch");
        var codex = world.Run("plan", "ANTIPHON", "--cli", "Codex", "--path", custom);
        codex.Exit.ShouldBe(0, codex.All);
        var lines = Lines(codex.Output);
        lines.ShouldContain("sibling:  " + custom);
        lines.ShouldContain("cli:      codex");
        lines.ShouldContain("  " + Path.Combine(custom, "AGENTS.md"));
        lines.ShouldContain("  hooks unverified - see CARD-0251 plan 1.2/1.3");
        lines.ShouldNotContain("  " + Path.Combine(custom, "CLAUDE.md"));
    }

    [Test]
    public void Setup_writes_the_claude_sibling_and_keeps_the_rest_of_claude_json()
    {
        var world = new SiblingWorkspaceWorld();
        world.Files.AddFile(world.ClaudeJson, new MockFileData(Encoding.UTF8.GetBytes(
            """{"numStartups":42,"projects":{"C:/other":{"allowedTools":[],"hasTrustDialogAccepted":true}},"tip":"café"}""")));

        var run = world.Run("setup", "antiphon");

        run.Exit.ShouldBe(0, run.All);
        run.Error.ShouldBeEmpty();
        Lines(run.Output).ShouldBe([
            "wrote sibling workspace: " + world.Sibling,
            "Next: PATCH the standing orchestrator WorkingDirectory to that path, then restart it.",
            "  scripts/antiphon orchestrator-workspace plan antiphon"]);

        var marker = world.Text(Path.Combine(world.Sibling, "antiphon.workspace.json"));
        marker.ShouldBe("{\"version\":1,\"checkout\":\"../antiphon\",\"project\":\"" + ProjectId + "\",\"cli\":\"claude\"}\n");
        var claudeMd = world.Text(Path.Combine(world.Sibling, "CLAUDE.md"));
        claudeMd.ShouldBe(OrchestratorWorkspace.ClaudeContext("../antiphon/AGENTS.md") + "\n");
        claudeMd.ShouldContain(OrchestratorWorkspace.LiveSettingsRule);
        claudeMd.ShouldEndWith("\n\n@../antiphon/AGENTS.md\n");
        world.Files.File.Exists(Path.Combine(world.Sibling, "AGENTS.md")).ShouldBeFalse();

        var settings = JsonNode.Parse(world.Text(Path.Combine(world.Sibling, ".claude", "settings.json")))!;
        var hook = "node \"" + Path.Combine(world.Checkout, "scripts", "hooks", "orchestrator-investigation-hook.mjs").Replace('\\', '/') + "\"";
        var pre = settings["hooks"]!["PreToolUse"]![0]!;
        pre["matcher"]!.GetValue<string>().ShouldBe("Read|Grep|Glob|Bash|PowerShell");
        pre["hooks"]![0]!["type"]!.GetValue<string>().ShouldBe("command");
        pre["hooks"]![0]!["command"]!.GetValue<string>().ShouldBe(hook);
        pre["hooks"]![0]!["timeout"]!.GetValue<int>().ShouldBe(5);
        var start = settings["hooks"]!["SessionStart"]![0]!;
        start["matcher"]!.GetValue<string>().ShouldBe("startup|resume|compact");
        start["hooks"]![0]!["command"]!.GetValue<string>().ShouldBe(hook);

        world.Junctions.ShouldBe([(Path.Combine(world.Sibling, ".claude", "skills"), Path.Combine(world.Checkout, ".claude", "skills"))]);

        var claudeJson = world.Text(world.ClaudeJson);
        claudeJson.ShouldContain("café", Case.Sensitive, "non-ASCII text is kept as written");
        var root = JsonNode.Parse(claudeJson)!;
        root["numStartups"]!.GetValue<int>().ShouldBe(42);
        root["projects"]!["C:/other"]!["hasTrustDialogAccepted"]!.GetValue<bool>().ShouldBeTrue();
        var entry = root["projects"]![world.Sibling.Replace('\\', '/')]!;
        entry["hasClaudeMdExternalIncludesApproved"]!.GetValue<bool>().ShouldBeTrue();
        entry["hasClaudeMdExternalIncludesWarningShown"]!.GetValue<bool>().ShouldBeTrue();
        foreach (var written in new[] { world.ClaudeJson, Path.Combine(world.Sibling, "CLAUDE.md"), Path.Combine(world.Sibling, "antiphon.workspace.json") })
            world.Files.File.ReadAllBytes(written).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }).ShouldBeFalse(written + " has no byte-order mark");

        // A second setup keeps an existing key's other fields and does not re-create an existing link.
        root["projects"]![world.Sibling.Replace('\\', '/')]!["allowedTools"] = new JsonArray("Read");
        world.Files.File.WriteAllText(world.ClaudeJson, root.ToJsonString());
        var again = world.Run("setup", "antiphon", "--verify");
        again.Exit.ShouldBe(0, again.All);
        Lines(again.Output).Last().ShouldBe("Claude has no offline inspect; run the S0 codeword canary for proof.");
        world.Junctions.Count.ShouldBe(1);
        var kept = JsonNode.Parse(world.Text(world.ClaudeJson))!["projects"]![world.Sibling.Replace('\\', '/')]!;
        kept["allowedTools"]![0]!.GetValue<string>().ShouldBe("Read");
        kept["hasClaudeMdExternalIncludesApproved"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Test]
    public void Setup_creates_claude_json_when_there_is_none()
    {
        var world = new SiblingWorkspaceWorld();
        var run = world.Run("setup", "antiphon");
        run.Exit.ShouldBe(0, run.All);
        var root = JsonNode.Parse(world.Text(world.ClaudeJson))!.AsObject();
        root.Select(property => property.Key).ShouldBe(["projects"]);
        root["projects"]!.AsObject().Single().Key.ShouldBe(world.Sibling.Replace('\\', '/'));
    }

    [Test]
    [Arguments("codex", "Verify with: codex debug prompt-input  (hooks unverified)")]
    [Arguments("grok", "Verify with: grok inspect --json  (hooks unverified)")]
    public void Setup_for_codex_and_grok_writes_agents_md_and_leaves_claude_alone(string cli, string verify)
    {
        var world = new SiblingWorkspaceWorld();
        var run = world.Run("setup", "antiphon", "--cli", cli, "--verify");

        run.Exit.ShouldBe(0, run.All);
        Lines(run.Output).ShouldBe([
            "hooks unverified - see CARD-0251 plan 1.2/1.3",
            "wrote sibling workspace: " + world.Sibling,
            "Next: PATCH the standing orchestrator WorkingDirectory to that path, then restart it.",
            "  scripts/antiphon orchestrator-workspace plan antiphon",
            verify]);
        var agents = world.Text(Path.Combine(world.Sibling, "AGENTS.md"));
        agents.ShouldBe(OrchestratorWorkspace.AgentsContext("../antiphon/AGENTS.md") + "\n");
        agents.ShouldContain("At session start, read ../antiphon/AGENTS.md in full before acting.");
        agents.ShouldContain(OrchestratorWorkspace.LiveSettingsRule);
        JsonNode.Parse(world.Text(Path.Combine(world.Sibling, "antiphon.workspace.json")))!["cli"]!.GetValue<string>().ShouldBe(cli);
        world.Files.File.Exists(Path.Combine(world.Sibling, "CLAUDE.md")).ShouldBeFalse();
        world.Files.Directory.Exists(Path.Combine(world.Sibling, ".claude")).ShouldBeFalse();
        world.Files.File.Exists(world.ClaudeJson).ShouldBeFalse();
        world.Junctions.ShouldBeEmpty();
    }

    [Test]
    public void Setup_refuses_without_a_checkout_and_warns_without_a_junction()
    {
        var bare = new SiblingWorkspaceWorld(localPath: null);
        var none = bare.Run("setup", "antiphon");
        none.Exit.ShouldBe(1);
        none.Error.Trim().ShouldBe("Project has no localRepositoryPath.");
        bare.Run("plan", "antiphon").Error.Trim().ShouldBe("Project has no localRepositoryPath.");

        var gone = new SiblingWorkspaceWorld(createCheckout: false);
        var missing = gone.Run("setup", "antiphon");
        missing.Exit.ShouldBe(1);
        missing.Error.Trim().ShouldBe("Checkout '" + gone.Checkout + "' does not exist.");
        gone.Files.Directory.Exists(gone.Sibling).ShouldBeFalse();

        // Off Windows (no junction seam) the rest is written and the missing link is a warning.
        var offWindows = new SiblingWorkspaceWorld();
        var warned = offWindows.Run(junction: false, "setup", "antiphon");
        warned.Exit.ShouldBe(0, warned.All);
        warned.Error.Trim().ShouldBe("WARNING: the skills junction " + Path.Combine(offWindows.Sibling, ".claude", "skills") + " -> "
            + Path.Combine(offWindows.Checkout, ".claude", "skills") + " was not created: directory junctions are created on Windows only");
        offWindows.Files.File.Exists(offWindows.ClaudeJson).ShouldBeTrue();

        var refusing = new SiblingWorkspaceWorld { JunctionRefusal = "Cannot create a file when that file already exists." };
        var refused = refusing.Run("setup", "antiphon");
        refused.Exit.ShouldBe(0, refused.All);
        refused.Error.ShouldContain("was not created: Cannot create a file when that file already exists.");

        // A checkout without .claude/skills needs no link.
        var noSkills = new SiblingWorkspaceWorld(skills: false);
        noSkills.Run(junction: false, "setup", "antiphon").Error.ShouldBeEmpty();

        // An unreadable ~/.claude.json stops the run (after the sibling's own files) and is left as it was.
        var broken = new SiblingWorkspaceWorld();
        broken.Files.AddFile(broken.ClaudeJson, new MockFileData("{not json"));
        var parse = broken.Run("setup", "antiphon");
        parse.Exit.ShouldBe(1);
        parse.Error.ShouldStartWith("Could not parse " + broken.ClaudeJson + ": ", Case.Sensitive);
        broken.Text(broken.ClaudeJson).ShouldBe("{not json");
        broken.Files.File.Exists(Path.Combine(broken.Sibling, "CLAUDE.md")).ShouldBeTrue();
        broken.Files.File.WriteAllText(broken.ClaudeJson, """{"projects":"none"}""");
        broken.Run("setup", "antiphon").Error.ShouldContain("projects is not an object");
    }

    [Test]
    public void Inspect_a_directory_and_a_project()
    {
        var world = new SiblingWorkspaceWorld();
        world.Run("setup", "antiphon").Exit.ShouldBe(0);
        var requests = world.Requests.Count;

        var directory = world.Run("inspect", world.Sibling);
        directory.Exit.ShouldBe(0, directory.All);
        Lines(directory.Output).ShouldBe([
            "directory: " + world.Sibling,
            "marker:    True",
            "checkout:  ../antiphon",
            "CLAUDE.md: True",
            "AGENTS.md: False",
            "For the server classification, pass a project name: inspect <project>."]);
        world.Requests.Count.ShouldBe(requests, "a directory is inspected without the API");

        var facts = JsonNode.Parse(world.Run("inspect", world.Sibling, "--json").Output)!;
        facts["directory"]!.GetValue<string>().ShouldBe(world.Sibling);
        facts["markerExists"]!.GetValue<bool>().ShouldBeTrue();
        facts["claudeMd"]!.GetValue<bool>().ShouldBeTrue();
        facts["agentsMd"]!.GetValue<bool>().ShouldBeFalse();
        facts["marker"]!["checkout"]!.GetValue<string>().ShouldBe("../antiphon");

        var plain = world.Run("inspect", world.Checkout);
        Lines(plain.Output).Take(3).ShouldBe(["directory: " + world.Checkout, "marker:    False", "CLAUDE.md: False"]);
        JsonNode.Parse(world.Run("inspect", world.Checkout, "--json").Output)!.AsObject().ContainsKey("marker").ShouldBeFalse();

        var project = world.Run("inspect", "antiphon");
        project.Exit.ShouldBe(0, project.All);
        Lines(project.Output).ShouldBe([
            "Project: antiphon (" + ProjectId + ")",
            "Warning      Orchestrator 'orch' runs in the checkout itself",
            "Proposed sibling: C:/src/antiphon-orchestrator. Preview with scripts/antiphon orchestrator-workspace plan."]);
        JsonNode.Parse(world.Run("inspect", "antiphon", "--json").Output)!["key"]!.GetValue<string>().ShouldBe("orchestrator-workspace");

        world.Readiness = """{"canDispatch":true,"checks":[{"key":"board","status":"Ok","summary":"fine"}]}""";
        var noRow = world.Run("inspect", "antiphon");
        noRow.Exit.ShouldBe(1);
        noRow.Output.Trim().ShouldBe("Project: antiphon (" + ProjectId + ")");
        noRow.Error.Trim().ShouldBe("readiness did not return an orchestrator-workspace row.");
        world.Run("inspect", "antiphon", "--json").Output.Trim().ShouldBe("null");

        world.Files.File.WriteAllText(Path.Combine(world.Sibling, "antiphon.workspace.json"), "{broken");
        var unreadable = world.Run("inspect", world.Sibling);
        unreadable.Exit.ShouldBe(1);
        unreadable.Error.ShouldStartWith("Could not parse " + Path.Combine(world.Sibling, "antiphon.workspace.json"), Case.Sensitive);
    }

    [Test]
    public void Acknowledge_posts_and_prints_the_row()
    {
        var world = new SiblingWorkspaceWorld();
        var run = world.Run("acknowledge", "antiphon");
        run.Exit.ShouldBe(0, run.All);
        run.Output.Trim().ShouldBe("Ok: Orchestrator workspace acknowledged");
        world.Requests.ShouldBe(["GET /api/projects", "POST /api/projects/" + ProjectId + "/acknowledge-orchestrator-workspace"]);
        world.Bodies.Last().ShouldBe("{}");

        JsonNode.Parse(world.Run("acknowledge", "antiphon", "--json").Output)!["canDispatch"]!.GetValue<bool>().ShouldBeTrue();

        world.Acknowledged = """{"canDispatch":true,"checks":[]}""";
        world.Run("acknowledge", "antiphon").Output.Trim().ShouldBe("acknowledged.");
    }

    [Test]
    public void A_project_is_found_by_guid_or_name()
    {
        var world = new SiblingWorkspaceWorld();
        world.Projects = $$"""
            [{"id":"{{ProjectId}}","name":"antiphon","localRepositoryPath":"{{world.Checkout.Replace("\\", "\\\\")}}"},
             {"id":"bbbbbbbb-1111-2222-3333-444444444444","name":"Antiphon","localRepositoryPath":"{{world.Checkout.Replace("\\", "\\\\")}}"},
             {"id":"cccccccc-1111-2222-3333-444444444444","name":"gym","localRepositoryPath":null},
             {"id":"dddddddd-1111-2222-3333-444444444444","name":"gym","localRepositoryPath":null}]
            """;
        JsonNode.Parse(world.Run("plan", ProjectId.ToUpperInvariant(), "--json").Output)!["projectId"]!.GetValue<string>().ShouldBe(ProjectId);
        JsonNode.Parse(world.Run("plan", "antiphon", "--json").Output)!["projectId"]!.GetValue<string>().ShouldBe(ProjectId);
        JsonNode.Parse(world.Run("plan", "Antiphon", "--json").Output)!["projectId"]!.GetValue<string>().ShouldBe("bbbbbbbb-1111-2222-3333-444444444444");
        foreach (var (needle, refusal) in new[]
        {
            ("ANTIPHON", "'ANTIPHON' matches 2 projects. Pass a project guid instead."),
            ("gym", "'gym' has multiple exact project matches. Pass a project guid instead."),
            ("nope", "No project matches 'nope'."),
        })
        {
            var refused = world.Run("acknowledge", needle);
            refused.Exit.ShouldBe(1, needle);
            refused.Error.Trim().ShouldBe(refusal);
        }
        world.Requests.ShouldNotContain(request => request.StartsWith("POST", StringComparison.Ordinal));

        world.Projects = null;
        var down = world.Run("plan", "antiphon");
        down.Exit.ShouldBe(1);
        down.Error.Trim().ShouldBe("Antiphon GET /api/projects failed: HTTP 503 unavailable");
    }

    [Test]
    public void Bad_command_lines_are_usage_errors()
    {
        var world = new SiblingWorkspaceWorld();
        foreach (var (args, message) in new (string[], string)[]
        {
            ([], "the first argument is the verb: inspect, plan, setup, acknowledge"),
            (["--json"], "the first argument is the verb: inspect, plan, setup, acknowledge"),
            (["move", "antiphon"], "the first argument is the verb (inspect, plan, setup, acknowledge), not 'move'"),
            (["plan"], "pass a project name or guid after plan"),
            (["inspect", "--json"], "pass a project name or guid after inspect, or a directory"),
            (["setup", "antiphon", "--json"], "--json does not apply to setup"),
            (["inspect", "antiphon", "--cli", "codex"], "--cli does not apply to inspect"),
            (["acknowledge", "antiphon", "--verify"], "--verify does not apply to acknowledge"),
            (["plan", "antiphon", "--cli", "vim"], "--cli must be one of claude, codex, grok, not 'vim'"),
            (["plan", "antiphon", "-Cli", "codex"], "unexpected argument '-Cli'"),
        })
        {
            var refused = world.Run(args);
            refused.Exit.ShouldBe(2, string.Join(" ", args));
            refused.Error.ShouldContain(message);
        }
        world.Requests.ShouldBeEmpty();
        OrchestratorWorkspaceOptions.Parse(["Setup", "antiphon"], null, null, null).Verb.ShouldBe("setup");
    }

    [Test]
    public void The_sibling_sits_beside_the_checkout_and_its_key_uses_forward_slashes()
    {
        var files = new MockFileSystem();
        var root = Path.Combine(Path.GetTempPath(), "orchestrator-sibling-" + Guid.NewGuid().ToString("N"));
        OrchestratorWorkspace.SiblingPath(files, Path.Combine(root, "repo")).ShouldBe(Path.Combine(root, "repo-orchestrator"));
        OrchestratorWorkspace.SiblingPath(files, Path.Combine(root, "repo") + Path.DirectorySeparatorChar).ShouldBe(Path.Combine(root, "repo-orchestrator"));
        OrchestratorWorkspace.ForwardSlashKey(files, Path.Combine(root, "repo-orchestrator") + Path.DirectorySeparatorChar)
            .ShouldBe(Path.Combine(root, "repo-orchestrator").Replace('\\', '/'));
        if (OperatingSystem.IsWindows())
            OrchestratorWorkspace.SiblingPath(files, @"C:\").ShouldBe(@"C:\orchestrator");
    }

    [Test]
    public void The_production_junction_runs_mklink_on_the_hosts_launcher()
    {
        var answers = new Queue<NightlyLaunchResult>([
            new NightlyLaunchResult { ExitCode = 0 },
            new NightlyLaunchResult { ExitCode = 1, LogText = "Cannot create a file when that file already exists.\n" },
            new NightlyLaunchResult { ExitCode = 5, LogText = "" }]);
        var launcher = new RecordingLauncher(_ => answers.Dequeue());
        var host = NightlyHost.Create(null, launcher, TextWriter.Null, TextWriter.Null, null, null, null, null);
        var junction = OrchestratorWorkspace.Mklink(host);

        junction(@"C:\x-orchestrator\.claude\skills", @"C:\x\.claude\skills").ShouldBeNull();
        junction("link", "target").ShouldBe("Cannot create a file when that file already exists.");
        junction("link", "target").ShouldBe("mklink exited 5");
        var launch = launcher.Launches[0];
        launch.FileName.ShouldBe("cmd.exe");
        launch.Arguments.ShouldBe(["/c", "mklink", "/J", @"C:\x-orchestrator\.claude\skills", @"C:\x\.claude\skills"]);
        launch.CaptureOutput.ShouldBeTrue();
        launch.Custody.ShouldBe(NightlyCustody.Watched);
        launch.TimeoutMilliseconds.ShouldBeGreaterThan(0);
    }

    private static string[] Lines(string output) => output.Replace("\r\n", "\n").Split('\n').Where(line => line.Length > 0).ToArray();

    // A checkout (with AGENTS.md and .claude/skills) and a user profile on an in-memory file system under a temp-path root;
    // the project API (one project named antiphon, its readiness and the acknowledge answer) on a recording transport; a
    // junction seam that records each link and creates it as a directory.
    private sealed class SiblingWorkspaceWorld
    {
        public MockFileSystem Files { get; } = new();
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "orchestrator-workspace-" + Guid.NewGuid().ToString("N"));
        public string Checkout => Path.Combine(Root, "antiphon");
        public string Sibling => Path.Combine(Root, "antiphon-orchestrator");
        public string Profile => Path.Combine(Root, "profile");
        public string ClaudeJson => Path.Combine(Profile, ".claude.json");
        public List<(string Link, string Target)> Junctions { get; } = [];
        public string? JunctionRefusal { get; init; }
        public List<string> Requests { get; } = [];
        public List<string?> Bodies { get; } = [];
        public string? Projects { get; set; }
        public string Readiness { get; set; } =
            """
            {"canDispatch":true,"checks":[{"key":"orchestrator-workspace","level":"Recommended","status":"Warning",
             "summary":"Orchestrator 'orch' runs in the checkout itself",
             "detail":"Proposed sibling: C:/src/antiphon-orchestrator. Preview with scripts/antiphon orchestrator-workspace plan."}]}
            """;
        public string Acknowledged { get; set; } =
            """{"canDispatch":true,"checks":[{"key":"orchestrator-workspace","status":"Ok","summary":"Orchestrator workspace acknowledged"}]}""";

        public SiblingWorkspaceWorld(string? localPath = "", bool createCheckout = true, bool skills = true)
        {
            if (createCheckout)
            {
                Files.AddFile(Path.Combine(Checkout, "AGENTS.md"), new MockFileData("# Agents\n"));
                if (skills) Files.AddFile(Path.Combine(Checkout, ".claude", "skills", "antiphon-delegate", "SKILL.md"), new MockFileData("skill\n"));
            }
            Files.Directory.CreateDirectory(Profile);
            var path = localPath == "" ? Checkout : localPath;
            Projects = new JsonArray(new JsonObject { ["id"] = ProjectId, ["name"] = "antiphon", ["localRepositoryPath"] = path }).ToJsonString();
        }

        public string Text(string path) => Files.File.ReadAllText(path, Encoding.UTF8);

        public BoardCommandRun.Result Run(params string[] args) => Run(true, args);

        public BoardCommandRun.Result Run(bool junction, params string[] args)
        {
            var http = new RecordingOperatorHttp(call =>
            {
                Requests.Add(call.Method + " " + call.Path);
                Bodies.Add(call.Body);
                if (call.Path == "/api/projects")
                    return Projects is null ? new NightlyReportHttpResponse(503, "unavailable") : new NightlyReportHttpResponse(200, Projects);
                if (call.Path.EndsWith("/readiness", StringComparison.Ordinal)) return new NightlyReportHttpResponse(200, Readiness);
                if (call.Path.EndsWith("/acknowledge-orchestrator-workspace", StringComparison.Ordinal)) return new NightlyReportHttpResponse(200, Acknowledged);
                return new NightlyReportHttpResponse(404, "{\"detail\":\"unexpected request\"}");
            });
            WorkspaceJunction? seam = junction
                ? (link, target) =>
                {
                    Junctions.Add((link, target));
                    if (JunctionRefusal is not null) return JunctionRefusal;
                    Files.Directory.CreateDirectory(link);
                    return null;
                }
                : null;
            return BoardCommandRun.Run(host => OrchestratorWorkspace.Run(OrchestratorWorkspaceOptions.Parse(args, BoardCommandRun.Api, null, Profile), host, Files,
                http.Transport, seam));
        }
    }
}
