using System.Text;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0187 S1: launch-script shape replacing <c>agent.start</c> — quoting, BOM, typed line,
/// poll/wrong-kind/timeout failures that tear down, script kept on failure and deleted on
/// success, pane shell must be PowerShell.
/// </summary>
[NotInParallel("HerdrLaunchShape")]
public class HerdrLaunchShapeTests
{
    [Test]
    public void Script_content_doubles_single_quotes_is_BOM_and_preserves_newline_args()
    {
        var newlineArg = "line one\nline two with 'sq' and \"dq\" and $5 and `tick`";
        var content = HerdrLaunchScript.BuildContent(
            @"C:\tools\it's\grok.exe",
            ["--no-alt-screen", "--rules", newlineArg, "--session-id", "abc"]);

        content.ShouldBe(
            "& 'C:\\tools\\it''s\\grok.exe' @('--no-alt-screen', '--rules', 'line one\nline two with ''sq'' and \"dq\" and $5 and `tick`', '--session-id', 'abc')");

        var tmp = Path.Combine(Path.GetTempPath(), $"herdr-launch-quote-{Guid.NewGuid():N}.launch.ps1");
        try
        {
            HerdrLaunchScript.Write(tmp, @"C:\tools\it's\grok.exe", ["--rules", newlineArg]);
            var bytes = File.ReadAllBytes(tmp);
            var bom = Encoding.UTF8.GetPreamble();
            bytes.Length.ShouldBeGreaterThan(bom.Length);
            bytes.Take(bom.Length).ToArray().ShouldBe(bom);
            var body = Encoding.UTF8.GetString(bytes, bom.Length, bytes.Length - bom.Length);
            body.ShouldContain(newlineArg.Replace("'", "''", StringComparison.Ordinal));
            body.ShouldNotContain("SECRET=");
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Test]
    public void Typed_command_is_ampersand_quoted_path()
    {
        var path = @"C:\logs\antiphon\session-runner\herdr\0123456789abcdef0123456789abcdef.launch.ps1";
        HerdrLaunchScript.TypedCommand(path).ShouldBe($"& '{path}'");
        HerdrLaunchScript.IsTypedCommand($"& '{path}'").ShouldBeTrue();
        HerdrLaunchScript.IsTypedCommand("hello nonce").ShouldBeFalse();
    }

    [Test]
    public async Task Success_types_exactly_the_script_line_deletes_the_script_and_does_not_call_agent_start()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();
        var scriptPath = HerdrLaunchScript.PathFor(settings.SessionLogPath, sessionId);
        var secret = $"env-secret-{Guid.NewGuid():N}";

        await using var runtime = BuildRuntime(settings, fake);
        await StartAsync(runtime, sessionId, settings.SessionLogPath, env: new Dictionary<string, string>
        {
            ["ANTIPHON_LAUNCH_SECRET"] = secret,
        });

        var sendText = fake.Requests
            .Where(r => r.GetProperty("method").GetString() == "pane.send_text")
            .Select(r => r.GetProperty("params").GetProperty("text").GetString())
            .ToList();
        sendText.ShouldContain(HerdrLaunchScript.TypedCommand(scriptPath));
        foreach (var t in sendText)
            (t ?? "").Contains(secret, StringComparison.Ordinal).ShouldBeFalse();

        fake.Requests.Any(r => r.GetProperty("method").GetString() == "agent.start")
            .ShouldBeFalse("CARD-0187: production launch never calls agent.start");
        File.Exists(scriptPath).ShouldBeFalse("script is deleted on success");

        var tabCreate = fake.Requests.First(r => r.GetProperty("method").GetString() == "tab.create");
        tabCreate.GetProperty("params").GetProperty("env").GetProperty("ANTIPHON_LAUNCH_SECRET")
            .GetString().ShouldBe(secret);

        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Poll_succeeds_after_detect_delay()
    {
        await using var fake = new FakeHerdrServer();
        fake.LaunchScriptDetectDelayMs = 400;
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();

        await using var runtime = BuildRuntime(settings, fake, launchDetectTimeoutMs: 5_000);
        var dto = await StartAsync(runtime, sessionId, settings.SessionLogPath);
        dto.Status.ShouldBe("Running");
        File.Exists(HerdrLaunchScript.PathFor(settings.SessionLogPath, sessionId)).ShouldBeFalse();

        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Wrong_kind_fails_keeps_script_and_tears_down()
    {
        await using var fake = new FakeHerdrServer();
        fake.LaunchScriptAgentKind = HerdrAgentKinds.Grok;
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();
        var scriptPath = HerdrLaunchScript.PathFor(settings.SessionLogPath, sessionId);

        await using var runtime = BuildRuntime(settings, fake);
        var ex = await Should.ThrowAsync<HerdrLaunchException>(() =>
            StartAsync(runtime, sessionId, settings.SessionLogPath, agentKind: HerdrAgentKinds.Claude));
        ex.Message.ShouldContain("grok");
        ex.Message.ShouldContain("claude");
        File.Exists(scriptPath).ShouldBeTrue("script is kept on failure");
        AssertTornDown(fake, scriptPath);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Timeout_fails_keeps_script_and_tears_down()
    {
        await using var fake = new FakeHerdrServer();
        fake.LaunchScriptAgentKind = null; // never detect
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();
        var scriptPath = HerdrLaunchScript.PathFor(settings.SessionLogPath, sessionId);

        await using var runtime = BuildRuntime(settings, fake, launchDetectTimeoutMs: 400);
        var ex = await Should.ThrowAsync<HerdrLaunchException>(() =>
            StartAsync(runtime, sessionId, settings.SessionLogPath));
        ex.Message.ShouldContain("did not detect");
        File.Exists(scriptPath).ShouldBeTrue();
        AssertTornDown(fake, scriptPath);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Shell_check_refuses_a_non_PowerShell_shell_pid()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();
        var scriptPath = HerdrLaunchScript.PathFor(settings.SessionLogPath, sessionId);

        await using var runtime = new SessionRunnerRuntime(
            Options.Create(settings),
            NullLogger<SessionRunnerRuntime>.Instance,
            new HerdrClient(new HerdrSettings { Enabled = true, Session = fake.Session, LaunchDetectTimeoutMs = 2_000 }),
            new NamedProcessProbe("cmd"));

        var ex = await Should.ThrowAsync<HerdrLaunchException>(() =>
            StartAsync(runtime, sessionId, settings.SessionLogPath));
        ex.Message.ShouldContain("cmd");
        ex.Message.ShouldContain("not PowerShell");
        File.Exists(scriptPath).ShouldBeFalse("script is written after the shell check");
        fake.Requests.Any(r => r.GetProperty("method").GetString() == "pane.send_text")
            .ShouldBeFalse("never type a launch line into a non-PowerShell shell");
        fake.Requests.Any(r => r.GetProperty("method").GetString() == "pane.close")
            .ShouldBeTrue("failure still tears down the pane we allocated");
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Unsupported_AgentKind_is_refused_before_contacting_herdr()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        await using var runtime = BuildRuntime(settings, fake);

        var ex = await Should.ThrowAsync<ArgumentException>(() =>
            runtime.StartAsync(
                new RunnerLaunchRequest(
                    Guid.NewGuid(),
                    "raw",
                    [],
                    new Dictionary<string, string>(),
                    settings.SessionLogPath,
                    Cols: 120,
                    Rows: 30,
                    Backend: SessionBackends.Herdr,
                    Herdr: new HerdrLaunchOptions(
                        WorkspaceKey: "none",
                        WorkspaceLabel: "x",
                        WorkspaceCwd: settings.SessionLogPath,
                        PaneTitle: "x",
                        AgentKind: "raw")),
                CancellationToken.None));
        ex.Message.ShouldContain("raw");
        fake.Requests.ShouldBeEmpty();
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public void Null_AgentKind_is_supported_as_claude()
    {
        HerdrAgentKinds.IsSupported(null).ShouldBeTrue();
        HerdrAgentKinds.IsSupported(HerdrAgentKinds.Claude).ShouldBeTrue();
        HerdrAgentKinds.IsSupported(HerdrAgentKinds.Grok).ShouldBeTrue();
        HerdrAgentKinds.IsSupported(HerdrAgentKinds.Codex).ShouldBeTrue();
        HerdrAgentKinds.IsSupported("raw").ShouldBeFalse();
        HerdrAgentKinds.IsSupported("Claude").ShouldBeFalse();
    }

    [Test]
    public async Task AgentSlug_is_sanitised_renamed_on_the_launched_pane_after_detection()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();
        var slug = "PM-Orchestrator-Grok-" + new string('x', 19);
        slug.Length.ShouldBeGreaterThan(32);
        var expected = HerdrPaneChild.SanitizeAgentName(slug);
        expected.Length.ShouldBe(32);

        await using var runtime = BuildRuntime(settings, fake);
        var dto = await StartAsync(runtime, sessionId, settings.SessionLogPath, agentSlug: slug);
        dto.Status.ShouldBe("Running");

        var paneId = fake.RequireAgentPaneId();
        var methods = fake.Requests.Select(r => r.GetProperty("method").GetString()).ToList();
        var renameIdx = methods.IndexOf("agent.rename");
        renameIdx.ShouldBeGreaterThan(-1);
        fake.Requests.Count(r => r.GetProperty("method").GetString() == "agent.rename").ShouldBe(1);
        var lastGetBeforeRename = methods.FindLastIndex(renameIdx, m => m == "pane.get");
        lastGetBeforeRename.ShouldBeGreaterThanOrEqualTo(0);
        methods.IndexOf("agent.list").ShouldBeLessThan(renameIdx);
        methods.Skip(renameIdx + 1).ShouldContain("pane.process_info");

        var rename = fake.Requests.First(r => r.GetProperty("method").GetString() == "agent.rename");
        rename.GetProperty("params").GetProperty("target").GetString().ShouldBe(paneId);
        rename.GetProperty("params").GetProperty("name").GetString().ShouldBe(expected);

        var pane = fake.Workspaces.SelectMany(w => w.Tabs.SelectMany(t => t.Panes))
            .Single(p => p.PaneId == paneId);
        pane.AgentName.ShouldBe(expected);
        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sessionId)).ShouldBeTrue();

        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Collision_suffixes_minus_2_and_warns_without_stealing()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        fake.SeedDetectedAgent("w-held:p1", HerdrAgentKinds.Claude, "pm-orchestrator-grok");
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();
        var logs = new List<string>();

        await using var runtime = BuildRuntime(settings, fake, logger: new ListLogger<SessionRunnerRuntime>(logs));
        var dto = await StartAsync(
            runtime, sessionId, settings.SessionLogPath, agentSlug: "pm-orchestrator-grok");
        dto.Status.ShouldBe("Running");

        var holder = fake.Workspaces.SelectMany(w => w.Tabs.SelectMany(t => t.Panes))
            .Single(p => p.PaneId == "w-held:p1");
        holder.AgentName.ShouldBe("pm-orchestrator-grok");

        var launched = fake.Workspaces.SelectMany(w => w.Tabs.SelectMany(t => t.Panes))
            .Single(p => p.Agent is not null && p.PaneId != "w-held:p1");
        launched.AgentName.ShouldBe("pm-orchestrator-grok-2");

        logs.ShouldContain(l =>
            l.Contains("[Warning]", StringComparison.Ordinal)
            && l.Contains("pm-orchestrator-grok", StringComparison.Ordinal)
            && l.Contains("w-held:p1", StringComparison.Ordinal));

        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Null_AgentSlug_does_not_list_or_rename()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();

        await using var runtime = BuildRuntime(settings, fake);
        var dto = await StartAsync(runtime, sessionId, settings.SessionLogPath, agentSlug: null);
        dto.Status.ShouldBe("Running");

        fake.Requests.Any(r => r.GetProperty("method").GetString() == "agent.list").ShouldBeFalse();
        fake.Requests.Any(r => r.GetProperty("method").GetString() == "agent.rename").ShouldBeFalse();

        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task RejectAgentRename_still_completes_the_launch_with_a_Warning()
    {
        await using var fake = new FakeHerdrServer { RejectAgentRename = "agent_name_taken" };
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();
        var scriptPath = HerdrLaunchScript.PathFor(settings.SessionLogPath, sessionId);
        var logs = new List<string>();

        await using var runtime = BuildRuntime(settings, fake, logger: new ListLogger<SessionRunnerRuntime>(logs));
        var dto = await StartAsync(
            runtime, sessionId, settings.SessionLogPath, agentSlug: "pm-orchestrator-grok");
        dto.Status.ShouldBe("Running");
        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sessionId)).ShouldBeTrue();
        File.Exists(scriptPath).ShouldBeFalse();
        logs.ShouldContain(l =>
            l.Contains("[Warning]", StringComparison.Ordinal)
            && l.Contains("agent_name_taken", StringComparison.Ordinal));

        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public void SanitizeAgentName_and_Suffix_respect_herdr_rules()
    {
        HerdrPaneChild.SanitizeAgentName("PM-Orchestrator-Grok").ShouldBe("pm-orchestrator-grok");
        HerdrPaneChild.SanitizeAgentName(new string('a', 40)).ShouldBe(new string('a', 32));
        HerdrPaneChild.SanitizeAgentName("2pm").ShouldBe("a2pm");
        var thirtyTwo = new string('b', 32);
        HerdrPaneChild.Suffix(thirtyTwo, 2).ShouldBe(new string('b', 30) + "-2");
    }

    private static SessionRunnerRuntime BuildRuntime(
        SessionRunnerSettings settings,
        FakeHerdrServer fake,
        int launchDetectTimeoutMs = 5_000,
        ILogger<SessionRunnerRuntime>? logger = null) =>
        new(
            Options.Create(settings),
            logger ?? NullLogger<SessionRunnerRuntime>.Instance,
            new HerdrClient(new HerdrSettings
            {
                Enabled = true,
                Session = fake.Session,
                LaunchDetectTimeoutMs = launchDetectTimeoutMs,
            }),
            new PowershellProcessProbe());

    private static SessionRunnerSettings BuildSettings() => new()
    {
        SessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-herdr-launch-{Guid.NewGuid():N}"),
        PtyHostLingerHours = 0.02,
    };

    private static Task<RunnerSessionDto> StartAsync(
        SessionRunnerRuntime runtime,
        Guid sessionId,
        string cwd,
        IReadOnlyDictionary<string, string>? env = null,
        string? agentKind = null,
        string? agentSlug = null) =>
        runtime.StartAsync(
            new RunnerLaunchRequest(
                sessionId,
                @"C:\tools\claude.exe",
                ["--dangerously-skip-permissions", "--append-system-prompt", "line one\nline two"],
                env ?? new Dictionary<string, string>(),
                cwd,
                Cols: 120,
                Rows: 30,
                Backend: SessionBackends.Herdr,
                Herdr: new HerdrLaunchOptions(
                    WorkspaceKey: $"test-{sessionId:N}"[..32],
                    WorkspaceLabel: "card0187-launch",
                    WorkspaceCwd: cwd,
                    PaneTitle: "card0187-launch",
                    AgentKind: agentKind,
                    AgentSlug: agentSlug)),
            CancellationToken.None);

    private static void AssertTornDown(FakeHerdrServer fake, string scriptPath)
    {
        File.Exists(scriptPath).ShouldBeTrue();
        fake.Requests.Any(r => r.GetProperty("method").GetString() == "pane.close")
            .ShouldBeTrue("StartHerdrAsync catch must KillAsync the pane");
    }

    private static void DeleteLogRoot(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort.
        }
    }
}
