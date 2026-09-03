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
    public void Fresh_root_script_prepends_quoted_set_location()
    {
        var content = HerdrLaunchScript.BuildContent(
            @"C:\tools\claude.exe",
            ["--session-id", "abc"],
            workingDirectory: @"D:\worktrees\it's\card");
        content.ShouldBe(
            "Set-Location -LiteralPath 'D:\\worktrees\\it''s\\card'\n& 'C:\\tools\\claude.exe' @('--session-id', 'abc')");
        HerdrLaunchScript.BuildContent(@"C:\tools\claude.exe", ["--session-id", "abc"])
            .ShouldBe("& 'C:\\tools\\claude.exe' @('--session-id', 'abc')");
    }

    [Test]
    public void Script_applies_env_clears_stale_names_and_resolves_env_tokens_before_quoting()
    {
        // CARD-0341: env lines precede the command (ordinal name order), stale names from the
        // previous launch are removed first, and a whole-argument $env:NAME token is resolved
        // from the env — PowerShell never expands it inside a single-quoted argument.
        var env = new Dictionary<string, string>
        {
            ["X_LLM_PROJECT"] = "PredictionMarkets",
            ["GROK_BASE_URL"] = "http://localhost:10746/v1",
            ["XAI_API_KEY"] = "llm-key-proxy",
            ["ODD"] = "it's\nmulti $line `tick` \"dq\"",
        };
        var content = HerdrLaunchScript.BuildContent(
            @"C:\Program Files\PowerShell\7\pwsh.exe",
            ["-NoProfile", "-File", @"C:\Users\x\.local\bin\gkp.ps1", "--project", "$env:X_LLM_PROJECT", "${env:xai_api_key}", "$env:MISSING", "$env:X_LLM_PROJECT/sub"],
            env,
            workingDirectory: @"D:\worktrees\card",
            clearNames: ["STALE_B", "STALE_A"]);

        content.ShouldBe(string.Join("\n",
            "Remove-Item -LiteralPath 'Env:STALE_A' -ErrorAction SilentlyContinue",
            "Remove-Item -LiteralPath 'Env:STALE_B' -ErrorAction SilentlyContinue",
            "Set-Item -LiteralPath 'Env:GROK_BASE_URL' -Value 'http://localhost:10746/v1'",
            "Set-Item -LiteralPath 'Env:ODD' -Value 'it''s\nmulti $line `tick` \"dq\"'",
            "Set-Item -LiteralPath 'Env:XAI_API_KEY' -Value 'llm-key-proxy'",
            "Set-Item -LiteralPath 'Env:X_LLM_PROJECT' -Value 'PredictionMarkets'",
            "Set-Location -LiteralPath 'D:\\worktrees\\card'",
            "& 'C:\\Program Files\\PowerShell\\7\\pwsh.exe' @('-NoProfile', '-File', 'C:\\Users\\x\\.local\\bin\\gkp.ps1', '--project', 'PredictionMarkets', 'llm-key-proxy', '$env:MISSING', '$env:X_LLM_PROJECT/sub')"));

        HerdrLaunchScript.TryReadEnvTokenName("$env:X_LLM_PROJECT", out var name).ShouldBeTrue();
        name.ShouldBe("X_LLM_PROJECT");
        HerdrLaunchScript.TryReadEnvTokenName("${ENV:My-Name}", out name).ShouldBeTrue();
        name.ShouldBe("My-Name");
        HerdrLaunchScript.TryReadEnvTokenName("--project=$env:X", out _).ShouldBeFalse();
        HerdrLaunchScript.TryReadEnvTokenName("$env:", out _).ShouldBeFalse();
        HerdrLaunchScript.TryReadEnvTokenName("literal", out _).ShouldBeFalse();
    }

    [Test]
    public void Redacted_script_hides_every_env_value_and_leaves_tokens_unresolved()
    {
        var env = new Dictionary<string, string>
        {
            ["XAI_API_KEY"] = "super-secret",
            ["X_LLM_PROJECT"] = "PredictionMarkets",
        };
        var content = HerdrLaunchScript.BuildContent(
            "pwsh.exe",
            ["--project", "$env:X_LLM_PROJECT"],
            env,
            clearNames: ["OLD"],
            redactEnv: true);

        content.ShouldBe(string.Join("\n",
            "Remove-Item -LiteralPath 'Env:OLD' -ErrorAction SilentlyContinue",
            $"Set-Item -LiteralPath 'Env:XAI_API_KEY' -Value '{HerdrLaunchScript.RedactedValue}'",
            $"Set-Item -LiteralPath 'Env:X_LLM_PROJECT' -Value '{HerdrLaunchScript.RedactedValue}'",
            "& 'pwsh.exe' @('--project', '$env:X_LLM_PROJECT')"));
        content.ShouldNotContain("super-secret");
        content.ShouldNotContain("PredictionMarkets");
    }

    [Test]
    public void StaleEnvNames_is_previous_minus_current_case_insensitive()
    {
        HerdrPaneChild.StaleEnvNames(null, new Dictionary<string, string>()).ShouldBeEmpty();
        HerdrPaneChild.StaleEnvNames(["A", "b", "C", "c"], new Dictionary<string, string> { ["B"] = "1" })
            .ShouldBe(["A", "C"]);
        HerdrPaneChild.StaleEnvNames(["A"], null).ShouldBe(["A"]);
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

        // CARD-0341: the env reaches the child through the script, never through the typed line.
        fake.LastLaunchScriptContent.ShouldNotBeNull();
        fake.LastLaunchScriptContent.ShouldContain(
            $"Set-Item -LiteralPath 'Env:ANTIPHON_LAUNCH_SECRET' -Value '{secret}'");
        fake.LastLaunchScriptContent.IndexOf("Set-Item", StringComparison.Ordinal)
            .ShouldBeLessThan(fake.LastLaunchScriptContent.IndexOf("& '", StringComparison.Ordinal));

        fake.Requests.Any(r => r.GetProperty("method").GetString() == "agent.start")
            .ShouldBeFalse("CARD-0187: production launch never calls agent.start");
        File.Exists(scriptPath).ShouldBeFalse("script is deleted on success");

        var workspaceCreate = fake.Requests.First(r => r.GetProperty("method").GetString() == "workspace.create");
        workspaceCreate.GetProperty("params").GetProperty("env").GetProperty("ANTIPHON_LAUNCH_SECRET")
            .GetString().ShouldBe(secret);
        fake.Requests.Any(r => r.GetProperty("method").GetString() == "tab.create")
            .ShouldBeFalse("CARD-0323: first launch uses workspace.create's root pane");

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

        var secret = $"env-secret-{Guid.NewGuid():N}";

        await using var runtime = BuildRuntime(settings, fake);
        var ex = await Should.ThrowAsync<HerdrLaunchException>(() =>
            StartAsync(
                runtime, sessionId, settings.SessionLogPath, agentKind: HerdrAgentKinds.Claude,
                env: new Dictionary<string, string> { ["ANTIPHON_LAUNCH_SECRET"] = secret }));
        ex.Message.ShouldContain("grok");
        ex.Message.ShouldContain("claude");
        File.Exists(scriptPath).ShouldBeTrue("script is kept on failure");
        // CARD-0341: kept for diagnosis, but never with the env values in it.
        var kept = File.ReadAllText(scriptPath);
        kept.ShouldContain($"Set-Item -LiteralPath 'Env:ANTIPHON_LAUNCH_SECRET' -Value '{HerdrLaunchScript.RedactedValue}'");
        kept.ShouldNotContain(secret);
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
    public async Task Gkp_launch_without_routing_env_is_refused_before_contacting_herdr()
    {
        // CARD-0341 ask 4: never let a gkp Grok launch fall through to grok.com. Nothing is
        // allocated, renamed, typed, or even written when the env cannot route it.
        await using var fake = new FakeHerdrServer();
        fake.LaunchScriptAgentKind = HerdrAgentKinds.Grok;
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();
        var scriptPath = HerdrLaunchScript.PathFor(settings.SessionLogPath, sessionId);
        await using var runtime = BuildRuntime(settings, fake);

        var ex = await Should.ThrowAsync<HerdrLaunchException>(() =>
            runtime.StartAsync(GkpRequest(sessionId, settings.SessionLogPath, new Dictionary<string, string>
            {
                ["X_LLM_PROJECT"] = "PredictionMarkets",
                // no GROK_BASE_URL, no dummy key
            }), CancellationToken.None));
        ex.Code.ShouldBe(HerdrProblemTypes.GkpEnvMissing);
        ex.Message.ShouldContain("GROK_BASE_URL");
        ex.Message.ShouldContain("XAI_API_KEY");
        fake.Requests.ShouldBeEmpty("a refused gkp launch never contacts herdr");
        File.Exists(scriptPath).ShouldBeFalse("nothing is written for a refused launch");
        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sessionId)).ShouldBeFalse();
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Gkp_launch_with_routing_env_types_a_script_carrying_the_resolved_project()
    {
        await using var fake = new FakeHerdrServer();
        fake.LaunchScriptAgentKind = HerdrAgentKinds.Grok;
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();
        await using var runtime = BuildRuntime(settings, fake);

        var dto = await runtime.StartAsync(GkpRequest(sessionId, settings.SessionLogPath, new Dictionary<string, string>
        {
            ["X_LLM_PROJECT"] = "PredictionMarkets",
            ["GROK_BASE_URL"] = "http://localhost:10746/v1",
            ["GROK_CLI_CHAT_PROXY_BASE_URL"] = "http://localhost:10746/v1",
            ["XAI_API_KEY"] = "llm-key-proxy",
        }), CancellationToken.None);
        dto.Status.ShouldBe("Running");

        var script = fake.LastLaunchScriptContent.ShouldNotBeNull();
        script.ShouldContain("Set-Item -LiteralPath 'Env:GROK_BASE_URL' -Value 'http://localhost:10746/v1'");
        script.ShouldContain("Set-Item -LiteralPath 'Env:XAI_API_KEY' -Value 'llm-key-proxy'");
        script.ShouldContain("Set-Item -LiteralPath 'Env:X_LLM_PROJECT' -Value 'PredictionMarkets'");
        // Ask 3: the single-quoted --project value is the project, not the literal token.
        script.ShouldContain("'--project', 'PredictionMarkets'");
        script.ShouldNotContain("$env:X_LLM_PROJECT");

        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    private static RunnerLaunchRequest GkpRequest(Guid sessionId, string cwd, IReadOnlyDictionary<string, string> env) =>
        new(
            sessionId,
            @"C:\Program Files\PowerShell\7\pwsh.exe",
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", @"C:\Users\x\.local\bin\gkp.ps1", "--project", "$env:X_LLM_PROJECT"],
            env,
            cwd,
            Cols: 120,
            Rows: 30,
            Backend: SessionBackends.Herdr,
            Herdr: new HerdrLaunchOptions(
                WorkspaceKey: $"gkp-{sessionId:N}"[..32],
                WorkspaceLabel: "card0341-gkp",
                WorkspaceCwd: cwd,
                PaneTitle: "PM-MavRef-DL-Grok",
                AgentKind: HerdrAgentKinds.Grok));

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

    [Test]
    public async Task Resume_with_a_last_pane_must_not_call_tab_create()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();
        const string workspaceKey = "card0224-resume";

        await using var runtime = BuildRuntime(settings, fake);
        await StartAsync(runtime, sessionId, settings.SessionLogPath, workspaceKey: workspaceKey);
        var paneId = fake.RequireAgentPaneId();
        runtime.SweepVanishedSessions(new DeadProcessProbe());
        fake.ClearDetectedAgent(paneId);
        fake.SetPaneProcessInfo(paneId, shellPid: 1);

        var dto = await StartAsync(runtime, sessionId, settings.SessionLogPath, workspaceKey: workspaceKey);
        dto.Status.ShouldBe("Running");
        fake.Requests.Count(r => r.GetProperty("method").GetString() == "tab.create").ShouldBe(0);
        HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sessionId))!
            .PaneId.ShouldBe(paneId);

        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task ReusePaneOfSessionId_targets_the_previous_sessions_pane_for_a_fresh_id()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var previousId = Guid.NewGuid();
        var freshId = Guid.NewGuid();
        const string workspaceKey = "card0224-reuse";

        await using var runtime = BuildRuntime(settings, fake);
        await StartAsync(runtime, previousId, settings.SessionLogPath, workspaceKey: workspaceKey);
        var paneId = fake.RequireAgentPaneId();
        runtime.SweepVanishedSessions(new DeadProcessProbe());
        fake.ClearDetectedAgent(paneId);
        fake.SetPaneProcessInfo(paneId, shellPid: 1);

        var dto = await StartAsync(
            runtime, freshId, settings.SessionLogPath,
            workspaceKey: workspaceKey, reusePaneOfSessionId: previousId);
        dto.Status.ShouldBe("Running");
        fake.Requests.Count(r => r.GetProperty("method").GetString() == "tab.create").ShouldBe(0);
        HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(settings.SessionLogPath, freshId))!
            .PaneId.ShouldBe(paneId);
        File.Exists(HerdrLastPane.PathFor(settings.SessionLogPath, previousId)).ShouldBeFalse();

        await runtime.KillAsync(freshId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Codex_occupied_pane_is_refused_even_with_the_right_kind()
    {
        await using var fake = new FakeHerdrServer();
        fake.LaunchScriptAgentKind = HerdrAgentKinds.Codex;
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();

        await using var runtime = BuildRuntime(settings, fake);
        await StartAsync(runtime, sessionId, settings.SessionLogPath, agentKind: HerdrAgentKinds.Codex);
        var paneId = fake.RequireAgentPaneId();
        runtime.SweepVanishedSessions(new DeadProcessProbe());
        fake.ClearDetectedAgent(paneId);
        fake.SetPaneProcessInfo(
            paneId, shellPid: 1,
            [(900, "cmd.exe", new[] { "codex.cmd", "--ask-for-approval", "never" }, (string?)null)]);
        fake.SeedDetectedAgent(paneId, HerdrAgentKinds.Codex);

        var ex = await Should.ThrowAsync<HerdrLaunchException>(() =>
            StartAsync(runtime, sessionId, settings.SessionLogPath, agentKind: HerdrAgentKinds.Codex));
        ex.Code.ShouldBe(HerdrLaunchException.CodePaneOccupied);
        ex.Message.ShouldContain(paneId);
        File.Exists(HerdrLastPane.PathFor(settings.SessionLogPath, sessionId)).ShouldBeTrue();
        fake.Requests.Any(r => r.GetProperty("method").GetString() == "pane.close").ShouldBeFalse();

        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Relaunch_in_place_never_calls_tab_rename_or_pane_split()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();

        await using var runtime = BuildRuntime(settings, fake);
        await StartAsync(runtime, sessionId, settings.SessionLogPath);
        var paneId = fake.RequireAgentPaneId();
        var afterFirst = fake.Requests.Count;
        runtime.SweepVanishedSessions(new DeadProcessProbe());
        fake.ClearDetectedAgent(paneId);
        fake.SetPaneProcessInfo(paneId, shellPid: 1);

        await StartAsync(runtime, sessionId, settings.SessionLogPath);
        var relaunch = fake.Requests.Skip(afterFirst).ToList();
        relaunch.Any(r => r.GetProperty("method").GetString() == "tab.rename").ShouldBeFalse();
        relaunch.Any(r => r.GetProperty("method").GetString() == "pane.split").ShouldBeFalse();
        relaunch.Any(r => r.GetProperty("method").GetString() == "tab.create").ShouldBeFalse();

        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Relaunch_in_place_reapplies_env_and_clears_the_names_it_no_longer_carries()
    {
        // CARD-0341: a reused pane (CARD-0224) gets no tab.create env; the script must carry it.
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();

        await using var runtime = BuildRuntime(settings, fake);
        await StartAsync(runtime, sessionId, settings.SessionLogPath, env: new Dictionary<string, string>
        {
            ["X_LLM_PROJECT"] = "Old",
            ["STALE_ONLY_FIRST"] = "1",
        });
        var paneId = fake.RequireAgentPaneId();
        HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sessionId))!
            .LaunchEnvNames.ShouldBe(["STALE_ONLY_FIRST", "X_LLM_PROJECT"]);

        runtime.SweepVanishedSessions(new DeadProcessProbe());
        HerdrLastPane.TryLoad(settings.SessionLogPath, sessionId)!
            .LaunchEnvNames.ShouldBe(["STALE_ONLY_FIRST", "X_LLM_PROJECT"]);
        fake.ClearDetectedAgent(paneId);
        fake.SetPaneProcessInfo(paneId, shellPid: 1);
        var afterFirst = fake.Requests.Count;

        var dto = await StartAsync(runtime, sessionId, settings.SessionLogPath, env: new Dictionary<string, string>
        {
            ["X_LLM_PROJECT"] = "New",
        });
        dto.Status.ShouldBe("Running");
        fake.Requests.Skip(afterFirst).Any(r => r.GetProperty("method").GetString() is "tab.create" or "pane.split")
            .ShouldBeFalse();

        var script = fake.LastLaunchScriptContent.ShouldNotBeNull();
        script.ShouldContain("Remove-Item -LiteralPath 'Env:STALE_ONLY_FIRST' -ErrorAction SilentlyContinue");
        script.ShouldContain("Set-Item -LiteralPath 'Env:X_LLM_PROJECT' -Value 'New'");
        script.ShouldNotContain("'Old'");
        script.ShouldNotContain("Set-Item -LiteralPath 'Env:STALE_ONLY_FIRST'");
        HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sessionId))!
            .LaunchEnvNames.ShouldBe(["X_LLM_PROJECT"]);

        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Fresh_workspace_uses_the_created_root_pane()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();
        const string paneTitle = "Agent-PM";

        await using var runtime = BuildRuntime(settings, fake);
        await StartAsync(runtime, sessionId, settings.SessionLogPath, paneTitle: paneTitle);

        CountMethod(fake, "workspace.create").ShouldBe(1);
        CountMethod(fake, "tab.create").ShouldBe(0);
        CountMethod(fake, "tab.rename").ShouldBe(1);
        var rename = fake.Requests.Single(r => r.GetProperty("method").GetString() == "tab.rename");
        rename.GetProperty("params").GetProperty("label").GetString().ShouldBe(paneTitle);

        fake.Workspaces.ShouldHaveSingleItem();
        fake.Workspaces[0].Tabs.ShouldHaveSingleItem();
        fake.Workspaces[0].Tabs[0].Panes.ShouldHaveSingleItem();
        fake.Workspaces[0].Tabs[0].Label.ShouldBe(paneTitle);
        fake.Workspaces[0].Tabs[0].Panes[0].Label.ShouldBe(paneTitle);

        var sidecar = HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sessionId));
        sidecar.ShouldNotBeNull();
        sidecar!.WorkspaceId.ShouldBe(fake.Workspaces[0].WorkspaceId);
        sidecar.TabId.ShouldBe(fake.Workspaces[0].Tabs[0].TabId);
        sidecar.PaneId.ShouldBe(fake.Workspaces[0].Tabs[0].Panes[0].PaneId);

        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Fresh_root_keeps_launch_cwd_and_environment()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();
        var workspaceCwd = Path.Combine(settings.SessionLogPath, "project");
        var requestCwd = Path.Combine(settings.SessionLogPath, "worktree");
        Directory.CreateDirectory(workspaceCwd);
        Directory.CreateDirectory(requestCwd);
        var secret = $"env-secret-{Guid.NewGuid():N}";

        await using var runtime = BuildRuntime(settings, fake);
        await StartAsync(
            runtime, sessionId, settings.SessionLogPath,
            env: new Dictionary<string, string> { ["ANTIPHON_LAUNCH_SECRET"] = secret },
            workspaceCwd: workspaceCwd,
            requestCwd: requestCwd);

        var create = fake.Requests.Single(r => r.GetProperty("method").GetString() == "workspace.create");
        create.GetProperty("params").GetProperty("cwd").GetString().ShouldBe(workspaceCwd);
        create.GetProperty("params").GetProperty("env").GetProperty("ANTIPHON_LAUNCH_SECRET")
            .GetString().ShouldBe(secret);
        fake.Workspaces[0].Tabs[0].Panes[0].Env!["ANTIPHON_LAUNCH_SECRET"].ShouldBe(secret);

        fake.LastLaunchScriptContent.ShouldNotBeNull();
        fake.LastLaunchScriptContent.ShouldContain($"Set-Location -LiteralPath {HerdrLaunchScript.Quote(requestCwd)}");
        fake.LastLaunchScriptContent.ShouldContain(
            $"Set-Item -LiteralPath 'Env:ANTIPHON_LAUNCH_SECRET' -Value '{secret}'");
        foreach (var text in fake.Requests
                     .Where(r => r.GetProperty("method").GetString() == "pane.send_text")
                     .Select(r => r.GetProperty("params").GetProperty("text").GetString() ?? ""))
        {
            text.Contains(secret, StringComparison.Ordinal).ShouldBeFalse();
            text.Contains("Set-Location", StringComparison.Ordinal).ShouldBeFalse();
        }

        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Unique_untagged_label_reuses_the_operator_workspace_without_stamping()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();
        const string label = "PredictionMarkets";
        fake.SeedWorkspace("wOp", label);

        await using var runtime = BuildRuntime(settings, fake);
        await StartAsync(
            runtime, sessionId, settings.SessionLogPath,
            workspaceKey: "project:pm",
            workspaceLabel: label);

        CountMethod(fake, "workspace.create").ShouldBe(0);
        var tabCreate = fake.Requests
            .Where(r => r.GetProperty("method").GetString() == "tab.create")
            .ShouldHaveSingleItem();
        tabCreate.GetProperty("params").GetProperty("workspace_id").GetString().ShouldBe("wOp");
        fake.Requests.Any(r =>
                r.GetProperty("method").GetString() == "workspace.report_metadata"
                && r.GetProperty("params").GetProperty("workspace_id").GetString() == "wOp")
            .ShouldBeFalse();
        fake.Workspaces.Single(w => w.WorkspaceId == "wOp").Tokens.ContainsKey("antiphon-ws")
            .ShouldBeFalse();
        fake.LastLaunchScriptContent.ShouldNotBeNull();
        fake.LastLaunchScriptContent.ShouldNotContain("Set-Location");

        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Ambiguous_or_foreign_label_match_creates_a_managed_workspace()
    {
        await using (var fake = new FakeHerdrServer())
        {
            fake.Start();
            await fake.WaitUntilListeningAsync();
            var settings = BuildSettings();
            var sessionId = Guid.NewGuid();
            const string label = "PredictionMarkets";
            fake.SeedWorkspace("wA", label);
            fake.SeedWorkspace("wB", label);

            await using var runtime = BuildRuntime(settings, fake);
            await StartAsync(
                runtime, sessionId, settings.SessionLogPath,
                workspaceKey: "project:pm",
                workspaceLabel: label);

            CountMethod(fake, "workspace.create").ShouldBe(1);
            fake.Workspaces.Count.ShouldBe(3);
            fake.Workspaces.Single(w => w.WorkspaceId == "wA").Tabs.Count.ShouldBe(1);
            fake.Workspaces.Single(w => w.WorkspaceId == "wB").Tabs.Count.ShouldBe(1);
            var created = fake.Workspaces.Single(w => w.WorkspaceId is not "wA" and not "wB");
            created.Tokens["antiphon-ws"].ShouldBe("project:pm");
            created.Tabs.ShouldHaveSingleItem();

            await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
            DeleteLogRoot(settings.SessionLogPath);
        }

        await using (var fake = new FakeHerdrServer())
        {
            fake.Start();
            await fake.WaitUntilListeningAsync();
            var settings = BuildSettings();
            var sessionId = Guid.NewGuid();
            const string label = "PredictionMarkets";
            fake.SeedWorkspace("wForeign", label, new Dictionary<string, string>
            {
                ["antiphon-ws"] = "project:other",
            });

            await using var runtime = BuildRuntime(settings, fake);
            await StartAsync(
                runtime, sessionId, settings.SessionLogPath,
                workspaceKey: "project:pm",
                workspaceLabel: label);

            CountMethod(fake, "workspace.create").ShouldBe(1);
            fake.Workspaces.Single(w => w.WorkspaceId == "wForeign").Tokens["antiphon-ws"]
                .ShouldBe("project:other");
            fake.Workspaces.Single(w => w.WorkspaceId == "wForeign").Tabs.Count.ShouldBe(1);
            var created = fake.Workspaces.Single(w => w.WorkspaceId != "wForeign");
            created.Tokens["antiphon-ws"].ShouldBe("project:pm");

            await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
            DeleteLogRoot(settings.SessionLogPath);
        }
    }

    [Test]
    public async Task Own_antiphon_ws_token_wins_over_untagged_same_label()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();
        const string label = "PredictionMarkets";
        const string key = "project:pm";
        fake.SeedWorkspace("wK", label, new Dictionary<string, string> { ["antiphon-ws"] = key });
        fake.SeedWorkspace("w2", label);

        await using var runtime = BuildRuntime(settings, fake);
        await StartAsync(
            runtime, sessionId, settings.SessionLogPath,
            workspaceKey: key,
            workspaceLabel: label);

        CountMethod(fake, "workspace.create").ShouldBe(0);
        var report = fake.Requests
            .Where(r => r.GetProperty("method").GetString() == "workspace.report_metadata")
            .ShouldHaveSingleItem();
        report.GetProperty("params").GetProperty("workspace_id").GetString().ShouldBe("wK");
        report.GetProperty("params").GetProperty("tokens").GetProperty("antiphon-ws").GetString()
            .ShouldBe(key);
        fake.Workspaces.Single(w => w.WorkspaceId == "wK").Tokens["antiphon-ws"].ShouldBe(key);
        fake.Workspaces.Single(w => w.WorkspaceId == "w2").Tokens.ContainsKey("antiphon-ws")
            .ShouldBeFalse();
        var tabCreate = fake.Requests
            .Where(r => r.GetProperty("method").GetString() == "tab.create")
            .ShouldHaveSingleItem();
        tabCreate.GetProperty("params").GetProperty("workspace_id").GetString().ShouldBe("wK");

        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    private static Task<RunnerSessionDto> StartAsync(
        SessionRunnerRuntime runtime,
        Guid sessionId,
        string cwd,
        IReadOnlyDictionary<string, string>? env = null,
        string? agentKind = null,
        string? agentSlug = null,
        string? workspaceKey = null,
        string? workspaceLabel = null,
        string? workspaceCwd = null,
        string? paneTitle = null,
        string? requestCwd = null,
        Guid? reusePaneOfSessionId = null) =>
        runtime.StartAsync(
            new RunnerLaunchRequest(
                sessionId,
                @"C:\tools\claude.exe",
                ["--dangerously-skip-permissions", "--append-system-prompt", "line one\nline two"],
                env ?? new Dictionary<string, string>(),
                requestCwd ?? cwd,
                Cols: 120,
                Rows: 30,
                Backend: SessionBackends.Herdr,
                Herdr: new HerdrLaunchOptions(
                    WorkspaceKey: workspaceKey ?? $"test-{sessionId:N}"[..32],
                    WorkspaceLabel: workspaceLabel ?? "card0187-launch",
                    WorkspaceCwd: workspaceCwd ?? cwd,
                    PaneTitle: paneTitle ?? "card0187-launch",
                    AgentKind: agentKind,
                    AgentSlug: agentSlug,
                    ReusePaneOfSessionId: reusePaneOfSessionId)),
            CancellationToken.None);

    private static int CountMethod(FakeHerdrServer fake, string method) =>
        fake.Requests.Count(r => r.GetProperty("method").GetString() == method);

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
