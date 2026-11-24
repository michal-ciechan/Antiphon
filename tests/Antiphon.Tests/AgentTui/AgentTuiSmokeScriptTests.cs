using System.IO.Abstractions.TestingHelpers;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.AgentTui;

/// <summary>
/// The AI Agent TUI profile smoke (the verify-agent-tui-profile command, Antiphon.Nightly's AgentTuiProfileSmoke), in process
/// since unit 33 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md (these rows read the text of
/// scripts/verify-agent-tui-profile.ps1). Every row runs on a fake API and SessionRunner, a fake process reader, a fake clock
/// and an in-memory file system: no agent TUI is started and no model turn is spent.
/// </summary>
[Category("Unit")]
public sealed class AgentTuiSmokeScriptTests
{
    private static readonly string Evidence = Path.Combine(Path.GetTempPath(), "agent-tui-smoke-evidence");

    [Test]
    public void Configuration_smoke_creates_the_profile_and_selects_it_without_a_live_session()
    {
        var world = new SmokeWorld();
        world.Run(new AgentTuiProfileSmokeOptions { SkipLiveMessage = true, EvidenceDir = Evidence }).ShouldBe(0, world.Output.ToString());
        var create = world.Single("POST", "/api/agent-tui/profiles");
        create.Body!["displayName"]!.GetValue<string>().ShouldBe("OpenCode Gateway");
        create.Body["kind"]!.GetValue<string>().ShouldBe("OpenCode");
        create.Body["authenticationMode"]!.GetValue<string>().ShouldBe("WrapperManaged");
        create.Body["arguments"]!.AsArray().Select(a => a!.GetValue<string>())
            .ShouldBe(new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", @"C:\Users\mike.ciechan\.local\bin\ocg.ps1", "--auto", "--mini" });
        create.Body.ContainsKey("expectedRevision").ShouldBeFalse();
        var patch = world.Single("PATCH", "/api/agents/a1");
        patch.Body!["tuiProfileId"]!.GetValue<string>().ShouldBe("p1");
        patch.Body["modelId"].ShouldBeNull("no --model-id is the runner default");
        patch.Body["workingDirectory"]!.GetValue<string>().ShouldBe(@"C:\work");
        world.Requests.ShouldNotContain(r => r.Path.EndsWith("/start", StringComparison.Ordinal) || r.Path.EndsWith("/stop", StringComparison.Ordinal));
        foreach (var name in new[] { "health.txt", "profiles-before.json", "profile.json", "models-refresh.json", "validation.json", "agent.json", "metrics.txt" })
            world.Files.File.Exists(Path.Combine(Evidence, name)).ShouldBeTrue(name);
        world.Output.ToString().ShouldContain("SkipLiveMessage set - configuration smoke complete.");
    }

    [Test]
    public void An_existing_profile_is_patched_at_its_revision()
    {
        var world = new SmokeWorld();
        world.Profiles = """[{"id":"p1","displayName":"opencode gateway","revision":7}]""";
        world.Run(new AgentTuiProfileSmokeOptions { SkipLiveMessage = true, EvidenceDir = Evidence }).ShouldBe(0, world.Output.ToString());
        world.Requests.ShouldNotContain(r => r.Method == "POST" && r.Path == "/api/agent-tui/profiles");
        world.Single("PATCH", "/api/agent-tui/profiles/p1").Body!["expectedRevision"]!.GetValue<int>().ShouldBe(7);
        world.Output.ToString().ShouldContain("Updating profile 'OpenCode Gateway' revision 7...");
    }

    [Test]
    [Arguments("failed-startup")]
    [Arguments("not-interactive")]
    [Arguments("no-startup-stage")]
    public void Validation_without_interactive_startup_fails_before_the_agent_is_touched(string shape)
    {
        var world = new SmokeWorld();
        world.Validation = shape switch
        {
            "failed-startup" => """{"stages":[{"name":"startup","status":"Failed"}],"suitability":{"interactive":true}}""",
            "not-interactive" => """{"stages":[{"name":"startup","status":"Passed"}],"suitability":{"interactive":false}}""",
            _ => """{"stages":[{"name":"version","status":"Passed"}],"suitability":{"interactive":true}}""",
        };
        world.Run(new AgentTuiProfileSmokeOptions { EvidenceDir = Evidence }).ShouldBe(1);
        world.Output.ToString().ShouldContain("Profile validation did not establish interactive startup readiness.");
        world.Requests.ShouldNotContain(r => r.Path.StartsWith("/api/agents", StringComparison.Ordinal));
    }

    [Test]
    public void An_unknown_agent_fails()
    {
        var world = new SmokeWorld();
        world.Run(new AgentTuiProfileSmokeOptions { AgentName = "Nobody", SkipLiveMessage = true, EvidenceDir = Evidence }).ShouldBe(1);
        world.Output.ToString().ShouldContain("Agent 'Nobody' not found");
        world.Requests.ShouldNotContain(r => r.Method == "PATCH" && r.Path.StartsWith("/api/agents/", StringComparison.Ordinal));
    }

    [Test]
    public void A_canary_in_retained_evidence_fails_at_once()
    {
        var world = new SmokeWorld { Metrics = "agent_tui_profiles 1\nleak sk-canary-701\n" };
        world.Run(new AgentTuiProfileSmokeOptions { Canary = "sk-canary-701", EvidenceDir = Evidence }).ShouldBe(1);
        world.Output.ToString().ShouldContain("Canary secret leaked into evidence file metrics.txt");
        world.Requests.ShouldNotContain(r => r.Path.EndsWith("/start", StringComparison.Ordinal), "no live session after a leak");
    }

    // The runner exposes the TUI process only on its third poll: the command line is read once it does, and the launch
    // arguments are checked before the composer is awaited.
    [Test]
    public void Smoke_waits_for_the_runner_process_before_reading_launch_arguments()
    {
        var world = new SmokeWorld { PidAfterPolls = 3 };
        world.Run(new AgentTuiProfileSmokeOptions { EvidenceDir = Evidence }).ShouldBe(0, world.Output.ToString());
        world.Requests.Count(r => r.Path == "/sessions/s1").ShouldBe(3);
        world.CommandLineReads.ShouldBe(new[] { 4242 });
        var launch = JsonNode.Parse(world.Files.File.ReadAllText(Path.Combine(Evidence, "launch-arguments.json")))!;
        launch["processId"]!.GetValue<int>().ShouldBe(4242);
        launch["modelArgumentPresent"]!.GetValue<bool>().ShouldBeFalse();
        world.IndexOf("GET", "/sessions/s1/snapshot").ShouldBeGreaterThan(world.LastIndexOf("GET", "/sessions/s1"));
    }

    [Test]
    public void Smoke_waits_for_the_OpenCode_composer_before_queueing_the_prompt()
    {
        var world = new SmokeWorld { ComposerAfterPolls = 4 };
        world.Run(new AgentTuiProfileSmokeOptions { EvidenceDir = Evidence }).ShouldBe(0, world.Output.ToString());
        var ready = world.Requests.Select((r, i) => (r, i)).Where(p => p.r.Path == "/sessions/s1/snapshot").Skip(3).First().i;
        world.IndexOf("POST", "/api/sessions/s1/messages").ShouldBeGreaterThan(ready, "the prompt waits for the composer");
        JsonNode.Parse(world.Files.File.ReadAllText(Path.Combine(Evidence, "ready-snapshot.json")))!["renderedScreen"]!.GetValue<string>()
            .ShouldContain(AgentTuiProfileSmoke.ComposerReady);
    }

    [Test]
    public void OpenCode_smoke_uses_the_queued_message_contract_and_checks_a_rendered_reply()
    {
        var world = new SmokeWorld();
        world.Run(new AgentTuiProfileSmokeOptions { EvidenceDir = Evidence }).ShouldBe(0, world.Output.ToString());
        var message = world.Single("POST", "/api/sessions/s1/messages");
        message.Body!["body"]!.GetValue<string>().ShouldBe("Reply with exactly: Atlas OpenCode default verified.");
        message.Body["mode"]!.GetValue<string>().ShouldBe("WhenIdle");
        world.Requests.ShouldNotContain(r => r.Path.EndsWith("/input", StringComparison.Ordinal));
        world.Single("POST", "/api/agents/a1/start").Body!.ToJsonString().ShouldBe("""{"fresh":true,"remoteControl":false}""");
        world.Files.File.Exists(Path.Combine(Evidence, "snapshot-latest.json")).ShouldBeTrue();
        world.Output.ToString().ShouldContain("SMOKE OK - profile='OpenCode Gateway' model='' reply verified.");
    }

    [Test]
    public void A_reply_that_never_renders_times_out()
    {
        var world = new SmokeWorld { ReplyText = "Atlas OpenCode default verified. (almost)" };
        world.Run(new AgentTuiProfileSmokeOptions { EvidenceDir = Evidence }).ShouldBe(1);
        world.Output.ToString().ShouldContain("Expected reply not observed within timeout: Atlas OpenCode default verified.");
        world.Requests.ShouldNotContain(r => r.Method == "GET" && r.Path == "/api/agents/a1", "no selection read without the reply");
    }

    [Test]
    [Arguments("pair", 0)]
    [Arguments("joined", 1)]
    [Arguments("absent", 1)]
    public void An_explicit_model_needs_the_separate_model_arguments(string shape, int exit)
    {
        var world = new SmokeWorld
        {
            CommandLine = shape switch
            {
                "pair" => "\"pwsh.exe\" \"-File\" \"ocg.ps1\" \"--auto\" \"--model\" \"gpt-x\"",
                "joined" => "\"pwsh.exe\" \"-File\" \"ocg.ps1\" \"--model=gpt-x\"",
                _ => "\"pwsh.exe\" \"-File\" \"ocg.ps1\" \"--auto\"",
            },
            SelectionModel = "gpt-x",
        };
        world.Run(new AgentTuiProfileSmokeOptions { ModelId = "gpt-x", EvidenceDir = Evidence }).ShouldBe(exit, world.Output.ToString());
        world.Single("PATCH", "/api/agents/a1").Body!["modelId"]!.GetValue<string>().ShouldBe("gpt-x");
        if (exit != 0) world.Output.ToString().ShouldContain("The explicit-model launch did not contain the expected separate model arguments.");
    }

    [Test]
    public void A_default_model_launch_with_a_model_argument_fails()
    {
        var world = new SmokeWorld { CommandLine = "\"pwsh.exe\" \"--model\" \"other\"" };
        world.Run(new AgentTuiProfileSmokeOptions { EvidenceDir = Evidence }).ShouldBe(1);
        world.Output.ToString().ShouldContain("The default-model launch unexpectedly contained a model argument.");
    }

    [Test]
    public void A_live_selection_that_is_not_the_profile_revision_fails()
    {
        var world = new SmokeWorld { SelectionRevision = "r0" };
        world.Run(new AgentTuiProfileSmokeOptions { EvidenceDir = Evidence }).ShouldBe(1);
        world.Output.ToString().ShouldContain("Live session selection does not match the requested profile revision and model.");
    }

    [Test]
    public void Off_Windows_the_launch_arguments_are_not_read()
    {
        var world = new SmokeWorld { IsWindows = false };
        world.Run(new AgentTuiProfileSmokeOptions { EvidenceDir = Evidence }).ShouldBe(0, world.Output.ToString());
        world.CommandLineReads.ShouldBeEmpty();
        world.Requests.ShouldNotContain(r => r.Path == "/sessions/s1");
    }

    [Test]
    public void The_command_line_is_parsed_in_kebab_case()
    {
        var parsed = AgentTuiProfileSmokeOptions.Parse(["--base-url", "http://localhost:17202/", "--model-id", "m", "--skip-live-message"], @"C:\temp");
        (parsed.BaseUrl, parsed.ModelId, parsed.SkipLiveMessage, parsed.SessionRunnerUrl).ShouldBe(("http://localhost:17202", "m", true, "http://localhost:17204"));
        parsed.EvidenceDir.ShouldStartWith(Path.Combine(@"C:\temp", "antiphon-agent-tui-smoke-"));
        Should.Throw<NightlyUsageException>(() => AgentTuiProfileSmokeOptions.Parse(["-BaseUrl", "x"], "t"));
        Should.Throw<NightlyUsageException>(() => AgentTuiProfileSmokeOptions.Parse(["--skip-live-message", "--skip-live-message"], "t"));
    }

    /// <summary>A fake API and SessionRunner with one agent and one profile; each request is recorded with its JSON body.</summary>
    private sealed class SmokeWorld
    {
        public sealed record Request(string Method, string Path, JsonObject? Body);

        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 24, 0, 0, 0, TimeSpan.Zero));
        public StringWriter Output { get; } = new();
        public MockFileSystem Files { get; } = new();
        public List<Request> Requests { get; } = [];
        public List<int> CommandLineReads { get; } = [];
        public string Profiles { get; set; } = "[]";
        public string Validation { get; set; } = """{"stages":[{"name":"startup","status":"Passed"}],"suitability":{"interactive":true}}""";
        public string Metrics { get; set; } = "agent_tui_profiles 1\n";
        public string CommandLine { get; set; } = "\"pwsh.exe\" \"-NoProfile\" \"-File\" \"ocg.ps1\" \"--auto\" \"--mini\"";
        public string ReplyText { get; set; } = "Atlas OpenCode default verified.";
        public string? SelectionModel { get; set; }
        public string SelectionRevision { get; set; } = "r1";
        public int PidAfterPolls { get; set; } = 1;
        public int ComposerAfterPolls { get; set; } = 1;
        public bool IsWindows { get; set; } = true;

        private int _sessionPolls;
        private int _snapshotPolls;
        private bool _queued;

        public int Run(AgentTuiProfileSmokeOptions options)
        {
            var host = new NightlyHost
            {
                Clock = Clock, Git = new FakeGit().Run, Launcher = new RecordingLauncher(_ => throw new InvalidOperationException("the smoke starts no process")),
                Processes = new FakeProcessTable(), Probe = new FakeHostProbe(), Out = Output, Error = Output, Trace = TextWriter.Null, Sleep = Clock.Advance,
            };
            return AgentTuiProfileSmoke.Run(options, host, Files, new AgentTuiProfileSmokeSeams
            {
                Http = Answer,
                CommandLine = pid => { CommandLineReads.Add(pid); return CommandLine; },
                IsWindows = IsWindows,
            });
        }

        public Request Single(string method, string path) => Requests.Where(r => r.Method == method && r.Path == path).ShouldHaveSingleItem(method + " " + path);

        public int IndexOf(string method, string path) => Requests.FindIndex(r => r.Method == method && r.Path == path);

        public int LastIndexOf(string method, string path) => Requests.FindLastIndex(r => r.Method == method && r.Path == path);

        private NightlyReportHttpResponse Answer(string method, string uri, IReadOnlyDictionary<string, string> headers, string? body)
        {
            var parsed = new Uri(uri);
            var path = parsed.AbsolutePath;
            Requests.Add(new Request(method, path, body is null ? null : JsonNode.Parse(body)!.AsObject()));
            var runner = parsed.Port == 17204;
            NightlyReportHttpResponse Ok(string text) => new(200, text);
            if (runner)
            {
                if (path == "/sessions/s1") return Ok(++_sessionPolls >= PidAfterPolls ? """{"pid":4242}""" : """{"pid":0}""");
                if (path == "/sessions/s1/snapshot")
                {
                    var screen = _queued ? "OpenCode\n  " + ReplyText + "  \n" : ++_snapshotPolls >= ComposerAfterPolls ? "OpenCode\n> Ask anything...\n" : "starting\n";
                    return Ok(new JsonObject { ["renderedScreen"] = screen }.ToJsonString());
                }
                return new(404, "");
            }
            switch (method + " " + path)
            {
                case "GET /health": return Ok("Healthy");
                case "GET /api/agent-tui/profiles": return Ok(Profiles);
                case "POST /api/agent-tui/profiles":
                case "PATCH /api/agent-tui/profiles/p1": return Ok("""{"id":"p1","revision":8,"revisionId":"r1"}""");
                case "POST /api/agent-tui/profiles/p1/models/refresh": return Ok("""{"models":[]}""");
                case "POST /api/agent-tui/profiles/p1/validate": return Ok(Validation);
                case "GET /api/agents": return Ok("""[{"id":"a1","name":"Atlas-Orchestrator","workingDirectory":"C:\\work","alwaysOn":false}]""");
                case "PATCH /api/agents/a1": return Ok("""{"id":"a1"}""");
                case "GET /metrics/agent-tui": return Ok(Metrics);
                case "POST /api/agents/a1/stop": return new(409, """{"error":"not running"}""");
                case "POST /api/agents/a1/start": return Ok("""{"liveSession":{"id":"s1"}}""");
                case "POST /api/sessions/s1/messages": _queued = true; return Ok("""{"queued":true}""");
                case "GET /api/sessions/s1/buffer": return Ok("""{"lines":[]}""");
                case "GET /api/agents/a1":
                    return Ok(new JsonObject
                    {
                        ["id"] = "a1",
                        ["liveSessionSelection"] = new JsonObject { ["tuiProfileRevisionId"] = SelectionRevision, ["effectiveModelId"] = SelectionModel, ["pendingRestart"] = false },
                    }.ToJsonString());
                default: return new(404, "");
            }
        }
    }
}
