using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>CARD-0382 V-3: runner backstop refuses unsafe Grok rules before any session or Herdr work.</summary>
[NotInParallel("HerdrLaunchShape")]
public sealed class GrokRulesRunnerRefusalTests
{
    [Test]
    public async Task Herdr_grok_multiline_rules_are_refused_before_herdr_is_contacted()
    {
        var sentinel = Sentinel();
        await AssertHerdrRefused(
            args: ["--always-approve", "--no-alt-screen", "--session-id", Guid.NewGuid().ToString("D"),
                "--rules", "line one\nline two " + sentinel],
            sentinel: sentinel,
            reason: GrokRulesArgvPolicy.ReasonLineBreak);
    }

    [Test]
    public async Task Herdr_grok_rules_equals_form_is_refused()
    {
        var sentinel = Sentinel();
        await AssertHerdrRefused(
            args: ["--rules=line one\nline two " + sentinel],
            sentinel: sentinel,
            reason: GrokRulesArgvPolicy.ReasonLineBreak);
    }

    [Test]
    public async Task Herdr_grok_append_system_prompt_alias_is_refused()
    {
        var sentinel = Sentinel();
        await AssertHerdrRefused(
            args: ["--append-system-prompt", "line one\nline two " + sentinel],
            sentinel: sentinel,
            reason: GrokRulesArgvPolicy.ReasonLineBreak,
            flag: GrokRulesArgvPolicy.AppendSystemPromptFlag);
    }

    [Test]
    public async Task Herdr_env_token_resolving_to_multiline_rules_is_refused()
    {
        var sentinel = Sentinel();
        await AssertHerdrRefused(
            args: ["--rules", "$env:RULES"],
            env: new Dictionary<string, string> { ["RULES"] = "line one\nline two " + sentinel },
            sentinel: sentinel,
            reason: GrokRulesArgvPolicy.ReasonLineBreak,
            envToken: "RULES");
    }

    [Test]
    public async Task Herdr_env_supplied_rules_flag_is_refused()
    {
        var sentinel = Sentinel();
        await AssertHerdrRefused(
            args: ["$env:FLAG", "line one\nline two " + sentinel],
            env: new Dictionary<string, string> { ["FLAG"] = "--rules" },
            sentinel: sentinel,
            reason: GrokRulesArgvPolicy.ReasonLineBreak);
    }

    [Test]
    public async Task Herdr_grok_oversized_rules_are_refused()
    {
        var sentinel = Sentinel();
        var payload = new string('x', 4097);
        await AssertHerdrRefused(
            args: ["--rules", payload],
            sentinel: sentinel,
            reason: GrokRulesArgvPolicy.ReasonTokenTooLong,
            extraMessage: ["4097", "4096"]);
    }

    [Test]
    public async Task Herdr_grok_wrapper_launch_with_multiline_rules_is_refused_by_kind_not_by_exe_name()
    {
        var sentinel = Sentinel();
        var settings = BuildSettings();
        await using var fake = new FakeHerdrServer();
        fake.LaunchScriptAgentKind = HerdrAgentKinds.Grok;
        fake.Start();
        await fake.WaitUntilListeningAsync();
        await using var runtime = BuildRuntime(settings, fake);
        var sessionId = Guid.NewGuid();
        var env = new Dictionary<string, string>
        {
            ["X_LLM_PROJECT"] = "PredictionMarkets",
            ["GROK_BASE_URL"] = "http://localhost:10746/v1",
            ["XAI_API_KEY"] = "llm-key-proxy",
        };
        var request = new RunnerLaunchRequest(
            sessionId,
            "pwsh.exe",
            ["-NoProfile", "-File", @"C:\Users\x\.local\bin\gkp.ps1", "--project", "$env:X_LLM_PROJECT",
                "--rules", "line one\nline two " + sentinel],
            env,
            settings.SessionLogPath,
            Cols: 120,
            Rows: 30,
            Backend: SessionBackends.Herdr,
            TranscriptFormat: TranscriptFormats.Grok,
            Herdr: GrokHerdr(sessionId, settings.SessionLogPath));

        var ex = await Should.ThrowAsync<GrokRulesLaunchException>(
            () => runtime.StartAsync(request, CancellationToken.None));
        AssertCommonRefusal(ex, runtime, fake, settings, sessionId, sentinel, GrokRulesArgvPolicy.ReasonLineBreak);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Pty_host_grok_multiline_rules_are_refused_before_a_session_is_registered()
    {
        var sentinel = Sentinel();
        var settings = BuildSettings();
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(settings),
            NullLogger<SessionRunnerRuntime>.Instance);
        var sessionId = Guid.NewGuid();
        var request = new RunnerLaunchRequest(
            sessionId,
            @"C:\tools\grok.exe",
            ["--always-approve", "--rules", "line one\nline two " + sentinel],
            new Dictionary<string, string>(),
            settings.SessionLogPath,
            Cols: 120,
            Rows: 30,
            TranscriptFormat: TranscriptFormats.Grok);

        var ex = await Should.ThrowAsync<GrokRulesLaunchException>(
            () => runtime.StartAsync(request, CancellationToken.None));
        ex.Code.ShouldBe(GrokRulesArgvPolicy.ProblemCode);
        ex.Message.ShouldContain(sessionId.ToString("D"));
        ex.Message.ShouldContain("--rules");
        ex.Message.ShouldContain(GrokRulesArgvPolicy.ReasonLineBreak);
        ex.Message.ShouldNotContain(sentinel);
        runtime.List().ShouldBeEmpty();
        await Should.ThrowAsync<KeyNotFoundException>(() => runtime.GetAsync(sessionId, CancellationToken.None));
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Herdr_grok_single_line_rules_proceed_to_herdr_unchanged()
    {
        var sentinel = Sentinel();
        var settings = BuildSettings();
        await using var fake = new FakeHerdrServer();
        fake.LaunchScriptAgentKind = HerdrAgentKinds.Grok;
        fake.Start();
        await fake.WaitUntilListeningAsync();
        await using var runtime = BuildRuntime(settings, fake);
        var sessionId = Guid.NewGuid();
        var rules = "keep it terse " + sentinel;
        var dto = await runtime.StartAsync(
            GrokIdentityRequest(sessionId, settings.SessionLogPath, ["--rules", rules]),
            CancellationToken.None);

        fake.Requests.ShouldNotBeEmpty();
        fake.LastLaunchScriptContent.ShouldNotBeNull();
        fake.LastLaunchScriptContent.ShouldContain($"'--rules', '{rules}'");
        dto.Status.ShouldBe("Running");

        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Herdr_claude_multiline_append_is_not_refused()
    {
        var settings = BuildSettings();
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        await using var runtime = BuildRuntime(settings, fake);
        var sessionId = Guid.NewGuid();
        var dto = await runtime.StartAsync(
            new RunnerLaunchRequest(
                sessionId,
                @"C:\tools\claude.exe",
                ["--append-system-prompt", "line one\nline two"],
                new Dictionary<string, string>(),
                settings.SessionLogPath,
                Cols: 120,
                Rows: 30,
                Backend: SessionBackends.Herdr,
                Herdr: new HerdrLaunchOptions(
                    WorkspaceKey: $"claude-{sessionId:N}"[..32],
                    WorkspaceLabel: "card0382-claude",
                    WorkspaceCwd: settings.SessionLogPath,
                    PaneTitle: "card0382-claude",
                    AgentKind: HerdrAgentKinds.Claude)),
            CancellationToken.None);

        fake.Requests.ShouldNotBeEmpty();
        dto.Status.ShouldBe("Running");
        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public void Grok_rules_refusal_maps_to_409_with_its_code()
    {
        var sentinel = Sentinel();
        var sessionId = Guid.NewGuid();
        var result = GrokRulesProblemMapper.Map(
            new GrokRulesLaunchException(
                $"Session {sessionId:D}: {GrokRulesArgvPolicy.ProblemCode}: --rules is line_break (occurrence 1)"));
        var problem = result.ShouldBeOfType<ProblemHttpResult>();
        problem.StatusCode.ShouldBe(409);
        problem.ProblemDetails.Type.ShouldBe(GrokRulesArgvPolicy.ProblemCode);
        var detail = problem.ProblemDetails.Detail.ShouldNotBeNull();
        detail.ShouldContain(GrokRulesArgvPolicy.ReasonLineBreak);
        detail.ShouldContain(sessionId.ToString("D"));
        detail.ShouldNotContain(sentinel);
    }

    private static async Task AssertHerdrRefused(
        IReadOnlyList<string> args,
        string sentinel,
        string reason,
        IReadOnlyDictionary<string, string>? env = null,
        string flag = GrokRulesArgvPolicy.RulesFlag,
        IReadOnlyList<string>? extraMessage = null,
        string? envToken = null)
    {
        var settings = BuildSettings();
        await using var fake = new FakeHerdrServer();
        fake.LaunchScriptAgentKind = HerdrAgentKinds.Grok;
        fake.Start();
        await fake.WaitUntilListeningAsync();
        await using var runtime = BuildRuntime(settings, fake);
        var sessionId = Guid.NewGuid();
        var request = GrokIdentityRequest(sessionId, settings.SessionLogPath, args, env);

        var ex = await Should.ThrowAsync<GrokRulesLaunchException>(
            () => runtime.StartAsync(request, CancellationToken.None));
        AssertCommonRefusal(ex, runtime, fake, settings, sessionId, sentinel, reason, flag, extraMessage, envToken);
        DeleteLogRoot(settings.SessionLogPath);
    }

    private static void AssertCommonRefusal(
        GrokRulesLaunchException ex,
        SessionRunnerRuntime runtime,
        FakeHerdrServer fake,
        SessionRunnerSettings settings,
        Guid sessionId,
        string sentinel,
        string reason,
        string flag = GrokRulesArgvPolicy.RulesFlag,
        IReadOnlyList<string>? extraMessage = null,
        string? envToken = null)
    {
        ex.Code.ShouldBe(GrokRulesArgvPolicy.ProblemCode);
        ex.Message.ShouldContain(sessionId.ToString("D"));
        ex.Message.ShouldContain(flag);
        ex.Message.ShouldContain(reason);
        ex.Message.ShouldNotContain(sentinel);
        if (envToken is not null)
        {
            ex.Message.ShouldContain(envToken);
            // The env VALUE is the multiline payload with the sentinel; naming the token is
            // allowed, echoing the value is not.
            ex.Message.ShouldNotContain("line one");
        }

        if (extraMessage is not null)
        {
            foreach (var part in extraMessage)
                ex.Message.ShouldContain(part);
        }

        fake.Requests.ShouldBeEmpty();
        fake.LastLaunchScriptContent.ShouldBeNull();
        runtime.List().ShouldBeEmpty();
        Should.Throw<KeyNotFoundException>(() => runtime.Get(sessionId));
        File.Exists(HerdrLaunchScript.PathFor(settings.SessionLogPath, sessionId)).ShouldBeFalse();
        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sessionId)).ShouldBeFalse();
        File.Exists(HerdrLastPane.PathFor(settings.SessionLogPath, sessionId)).ShouldBeFalse();
    }

    private static RunnerLaunchRequest GrokIdentityRequest(
        Guid sessionId,
        string cwd,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? env = null) =>
        new(
            sessionId,
            @"C:\tools\grok.exe",
            args,
            env ?? new Dictionary<string, string>(),
            cwd,
            Cols: 120,
            Rows: 30,
            Backend: SessionBackends.Herdr,
            TranscriptFormat: TranscriptFormats.Grok,
            Herdr: GrokHerdr(sessionId, cwd));

    private static HerdrLaunchOptions GrokHerdr(Guid sessionId, string cwd) =>
        new(
            WorkspaceKey: $"grok-{sessionId:N}"[..32],
            WorkspaceLabel: "card0382-grok",
            WorkspaceCwd: cwd,
            PaneTitle: "card0382-grok",
            AgentKind: HerdrAgentKinds.Grok);

    private static SessionRunnerRuntime BuildRuntime(SessionRunnerSettings settings, FakeHerdrServer fake) =>
        new(
            Options.Create(settings),
            NullLogger<SessionRunnerRuntime>.Instance,
            new HerdrClient(new HerdrSettings
            {
                Enabled = true,
                Session = fake.Session,
                LaunchDetectTimeoutMs = 5_000,
            }),
            new PowershellProcessProbe());

    private static SessionRunnerSettings BuildSettings() => new()
    {
        SessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-grok-rules-{Guid.NewGuid():N}"),
        PtyHostLingerHours = 0.02,
    };

    private static string Sentinel() => "card0382-sentinel-" + Guid.NewGuid().ToString("N");

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
