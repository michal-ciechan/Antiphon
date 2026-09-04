using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Tests.TestHelpers;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0084 S2's caller-facing half: <c>scripts/delegate.ps1 -Kind</c>. The script is what an
/// agent actually runs, so these drive the REAL script under pwsh against a stub server and assert
/// on the JSON body it posts — a string-match on the source would pass just as happily if the flag
/// never reached the request.
///
/// <para>The one thing that must not regress is the omitted case: a caller who never heard of
/// -Kind must produce byte-for-byte the request they produced before the flag existed, because the
/// server would then resolve a kind from the role policy that the caller did not choose.</para>
/// </summary>
[Category("Integration")]
public sealed class DelegateScriptKindTests
{
    [Test]
    public async Task Kind_Grok_is_posted_as_agentKind()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "-Role", "Test", "-Goal", "run the suite", "-Kind", "Grok");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("agentKind").GetString().ShouldBe("Grok");
        body.RootElement.GetProperty("kind").GetString().ShouldBe("Worker", "-Kind is a different axis from worker/orchestrator");
    }

    [Test]
    public async Task an_omitted_Kind_sends_no_agentKind_at_all()
    {
        // Absent, not "ClaudeCode": the field being missing is what lets the role policy decide,
        // and it is the difference between "the caller chose Claude" and "the caller said nothing".
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "-Role", "Test", "-Goal", "run the suite");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.TryGetProperty("agentKind", out _)
            .ShouldBeFalse("an omitted -Kind must leave the request exactly as it was before the flag existed");
    }

    [Test]
    public async Task Kind_Codex_is_posted_as_agentKind()
    {
        // CARD-0099 S3. Until this slice, Codex was the value this file used as its EXAMPLE of a
        // refused kind — the ValidateSet is the caller-facing half of the allowlist, so widening one
        // without the other leaves the flag rejected at the prompt no matter what the server admits.
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "-Role", "Test", "-Goal", "run the suite", "-Kind", "Codex");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("agentKind").GetString().ShouldBe("Codex");
        body.RootElement.GetProperty("kind").GetString().ShouldBe("Worker", "-Kind is a different axis from worker/orchestrator");
    }

    [Test]
    public async Task an_undelegatable_Kind_is_refused_by_the_script_before_any_request()
    {
        // ValidateSet is the cheap half of the allowlist: a typo costs nothing, and the caller
        // finds out at the prompt rather than through a 422 the server had to compose.
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "-Role", "Test", "-Goal", "run the suite", "-Kind", "OpenCode");

        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldContain("OpenCode");
        server.RequestCount.ShouldBe(0, "a rejected flag must not reach the server");
    }

    [Test]
    public async Task the_scripts_ValidateSet_is_exactly_the_servers_allowlist()
    {
        // The two halves are separate files in separate languages, and CARD-0099 is the second time
        // they had to move together. A ValidateSet narrower than the server silently makes a kind
        // undispatchable; a ValidateSet wider than it turns a clean refusal into a 422 round trip.
        var lines = await File.ReadAllLinesAsync(
            Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "delegate.ps1"));
        var line = lines
            .Select(l => l.Trim())
            .First(l => l.StartsWith("[ValidateSet('ClaudeCode'", StringComparison.Ordinal));

        foreach (var kind in AgentTaskService.DelegatableKinds)
            line.ShouldContain($"'{kind}'");
        foreach (var kind in Enum.GetValues<AgentKind>())
        {
            if (AgentTaskService.DelegatableKinds.Contains(kind))
                continue;
            line.ShouldNotContain($"'{kind}'");
        }
    }

    [Test]
    public async Task the_resolved_kind_is_echoed_back_to_the_caller()
    {
        // The caller may have chosen nothing and still be running on Grok (a role promoted in
        // config), so the ECHO comes from the server's answer, not from the flag.
        using var server = new StubApi(agentKind: "Grok");
        var run = await RunDelegateAsync(server, "-Role", "Code", "-Goal", "write it");

        run.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldContain("Grok");
    }

    [Test]
    public async Task a_ClaudeCode_task_is_announced_exactly_as_it_always_was()
    {
        using var server = new StubApi(agentKind: "ClaudeCode");
        var run = await RunDelegateAsync(server, "-Role", "Test", "-Goal", "run the suite");

        run.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldContain("queued task");
        run.Output.ShouldNotContain("ClaudeCode", customMessage: "the default kind is not news");
    }

    [Test]
    public async Task Complexity_Hard_is_posted_as_complexity()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(
            server, "-Role", "Plan", "-Goal", "plan it", "-Complexity", "Hard");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("complexity").GetString().ShouldBe("Hard");
        body.RootElement.TryGetProperty("refuseIfExhausted", out _)
            .ShouldBeFalse("an omitted -RefuseIfExhausted must leave the request as it was");
    }

    [Test]
    public async Task an_omitted_Complexity_sends_nothing_at_all()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "-Role", "Plan", "-Goal", "plan it");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.TryGetProperty("complexity", out _)
            .ShouldBeFalse("an omitted -Complexity must leave the request exactly as it was before the flag existed");
    }

    [Test]
    public async Task Complexity_plus_Kind_is_refused_locally()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(
            server, "-Role", "Plan", "-Goal", "plan it", "-Complexity", "Hard", "-Kind", "Grok");

        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldContain("never silently rerouted");
        server.RequestCount.ShouldBe(0);
    }

    [Test]
    public async Task Reroute_posts_kind_and_level()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(
            server, "-Reroute", "11111111-1111-1111-1111-111111111111",
            "-Kind", "Grok", "-Level", "Frontier");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastPath.ShouldBe("/api/agent-tasks/11111111-1111-1111-1111-111111111111/reroute");
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("agentKind").GetString().ShouldBe("Grok");
        body.RootElement.GetProperty("modelLevel").GetString().ShouldBe("Frontier");
        run.Output.ShouldContain("rerouted");
    }

    [Test]
    public async Task RefuseIfExhausted_is_posted_only_when_set()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(
            server, "-Role", "Plan", "-Goal", "plan it", "-Complexity", "Easy", "-RefuseIfExhausted");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("refuseIfExhausted").GetBoolean().ShouldBeTrue();
        body.RootElement.GetProperty("complexity").GetString().ShouldBe("Easy");
    }

    [Test]
    public async Task Authority_is_posted_as_authority()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(
            server, "-Role", "Code", "-Goal", "do the remaining epics",
            "-Authority", "start the remaining Coesite downloader epics one after another");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("authority").GetString()
            .ShouldBe("start the remaining Coesite downloader epics one after another");
    }

    [Test]
    public async Task an_omitted_Authority_sends_nothing_at_all()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "-Role", "Code", "-Goal", "do the remaining epics");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.TryGetProperty("authority", out _)
            .ShouldBeFalse("an omitted -Authority must leave the request exactly as it was before the flag existed");
    }

    [Test]
    public async Task Continue_posts_to_continue_and_prints_the_success_line()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "-Continue", "1234abcd");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastPath.ShouldBe("/api/agent-tasks/1234abcd/continue");
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("origin").GetString().ShouldBe("Cli");
        run.Output.ShouldContain("Continued task 1234abcd with its standing authority.");
    }

    [Test]
    public async Task IgnoreSubscriptionQuota_sends_ignoreSubscriptionQuota_true()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(
            server, "-Role", "Test", "-Goal", "run anyway", "-IgnoreSubscriptionQuota");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("ignoreSubscriptionQuota").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task EnvOverride_is_posted_as_launchEnvOverride()
    {
        using var server = new StubApi();
        var run = await RunDelegateCommandAsync(
            server,
            "-Role Test -Goal 'run the suite' -EnvOverride @{ ANTHROPIC_BASE_URL = 'http://proxy:8080'; ANTHROPIC_API_KEY = '{{key:proxy-key}}' }");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        var overlay = body.RootElement.GetProperty("launchEnvOverride");
        overlay.GetProperty("ANTHROPIC_BASE_URL").GetString().ShouldBe("http://proxy:8080");
        overlay.GetProperty("ANTHROPIC_API_KEY").GetString().ShouldBe("{{key:proxy-key}}");
    }

    [Test]
    public async Task an_omitted_EnvOverride_sends_no_launchEnvOverride_at_all()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "-Role", "Test", "-Goal", "run the suite");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.TryGetProperty("launchEnvOverride", out _)
            .ShouldBeFalse("an omitted -EnvOverride must leave the request exactly as it was before the flag existed");
    }

    [Test]
    public async Task a_live_LLM_project_is_forwarded_as_inheritedLlmEnv()
    {
        using var server = new StubApi();
        var run = await DelegateScriptRunner.RunAsync(
            server.BaseUrl,
            new Dictionary<string, string?>
            {
                ["X_LLM_PROJECT"] = "PredictionMarkets",
                ["X_LLM_KEY"] = "pm-key",
            },
            "-Role", "Test", "-Goal", "run the suite");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        var inherited = body.RootElement.GetProperty("inheritedLlmEnv");
        inherited.GetProperty("X_LLM_PROJECT").GetString().ShouldBe("PredictionMarkets");
        inherited.GetProperty("X_LLM_KEY").GetString().ShouldBe("pm-key");
    }

    [Test]
    public async Task NoInheritEnv_omits_the_live_LLM_env_snapshot()
    {
        using var server = new StubApi();
        var run = await DelegateScriptRunner.RunAsync(
            server.BaseUrl,
            new Dictionary<string, string?> { ["X_LLM_PROJECT"] = "PredictionMarkets" },
            "-Role", "Test", "-Goal", "run the suite", "-NoInheritEnv");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.TryGetProperty("inheritedLlmEnv", out _).ShouldBeFalse();
    }

    [Test]
    public async Task EnvOverride_keeps_its_own_LLM_project_out_of_inheritedLlmEnv()
    {
        using var server = new StubApi();
        var run = await RunDelegateCommandAsync(
            server,
            "-Role Test -Goal 'run the suite' -EnvOverride @{ X_LLM_PROJECT = 'override' }",
            new Dictionary<string, string?>
            {
                ["X_LLM_PROJECT"] = "from-shell",
                ["X_LLM_KEY"] = "pm-key",
            });

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("launchEnvOverride").GetProperty("X_LLM_PROJECT").GetString().ShouldBe("override");
        var inherited = body.RootElement.GetProperty("inheritedLlmEnv");
        inherited.TryGetProperty("X_LLM_PROJECT", out _).ShouldBeFalse();
        inherited.GetProperty("X_LLM_KEY").GetString().ShouldBe("pm-key");
    }

    [Test]
    public async Task an_omitted_IgnoreSubscriptionQuota_sends_no_ignoreSubscriptionQuota_at_all()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "-Role", "Test", "-Goal", "run the suite");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.TryGetProperty("ignoreSubscriptionQuota", out _)
            .ShouldBeFalse("the flag is sent only when chosen, matching -Kind / -ExpectAbout");
    }

    [Test]
    public async Task AllowUnauthenticatedProvider_sends_allowUnauthenticatedProvider_true()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(
            server, "-Role", "Code", "-Kind", "Grok", "-Goal", "queue anyway",
            "-AllowUnauthenticatedProvider");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("allowUnauthenticatedProvider").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task an_omitted_AllowUnauthenticatedProvider_sends_no_flag()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "-Role", "Test", "-Goal", "run the suite");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.TryGetProperty("allowUnauthenticatedProvider", out _)
            .ShouldBeFalse();
    }

    [Test]
    public async Task IgnoreModelDisabled_sends_ignoreModelDisabled_true()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(
            server, "-Role", "Test", "-Goal", "queue anyway", "-IgnoreModelDisabled");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("ignoreModelDisabled").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task an_omitted_IgnoreModelDisabled_sends_no_ignoreModelDisabled_at_all()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "-Role", "Test", "-Goal", "run the suite");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.TryGetProperty("ignoreModelDisabled", out _)
            .ShouldBeFalse("the flag is sent only when chosen, matching -IgnoreSubscriptionQuota");
    }

    [Test]
    public async Task IgnoreConcurrencyLimit_sends_ignoreConcurrencyLimit_true()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(
            server, "-Role", "Test", "-Goal", "run in parallel", "-IgnoreConcurrencyLimit");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("ignoreConcurrencyLimit").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task an_omitted_IgnoreConcurrencyLimit_sends_no_ignoreConcurrencyLimit_at_all()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "-Role", "Test", "-Goal", "run the suite");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.TryGetProperty("ignoreConcurrencyLimit", out _)
            .ShouldBeFalse("the flag is sent only when chosen, matching -IgnoreSubscriptionQuota");
    }

    [Test]
    public async Task WorktreeHealth_posts_and_prints_findings_without_pruning()
    {
        using var server = new StubApi(mode: StubApi.Mode.WorktreeHealth);
        var run = await RunDelegateAsync(server, "-WorktreeHealth");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastPath.ShouldBe("/api/agent-tasks/worktree-health");
        server.LastMethod.ShouldBe("POST");
        run.Output.ShouldContain("feat/card-task-aabbccdd");
        run.Output.ShouldContain("detection only");
        run.Output.ShouldContain("nothing pruned");
        run.Output.ShouldNotContain("worktree remove");
    }

    [Test]
    public async Task WorktreeHealth_with_no_findings_says_so()
    {
        using var server = new StubApi(mode: StubApi.Mode.WorktreeHealthEmpty);
        var run = await RunDelegateAsync(server, "-WorktreeHealth");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastPath.ShouldBe("/api/agent-tasks/worktree-health");
        run.Output.ShouldContain("No stuck feat/card-task-* worktrees.");
    }

    // ---- harness -------------------------------------------------------------------------------

    // The script runner itself lives in DelegateScriptRunner so S6's end-to-end test invokes
    // delegate.ps1 exactly the way this one does — same host flags, same environment, same
    // argument-list quoting.
    private static Task<(int ExitCode, string Output)> RunDelegateAsync(StubApi server, params string[] args) =>
        DelegateScriptRunner.RunAsync(server.BaseUrl, args);

    /// <summary>
    /// <c>-File</c> cannot bind a hashtable literal (every argument is a string). An agent at a
    /// prompt writes <c>-EnvOverride @{ ... }</c>, so this path drives the script via
    /// <c>-Command</c> the same way.
    /// </summary>
    private static async Task<(int ExitCode, string Output)> RunDelegateCommandAsync(
        StubApi server,
        string argumentTail,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var scriptPath = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "delegate.ps1");
        var startInfo = new System.Diagnostics.ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add($"& '{scriptPath}' {argumentTail}");
        startInfo.Environment["ANTIPHON_API"] = server.BaseUrl.TrimEnd('/');
        startInfo.Environment["ANTIPHON_TASK_TOKEN"] = string.Empty;
        if (environment is not null)
        {
            foreach (var (name, value) in environment)
                startInfo.Environment[name] = value;
        }

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout + await stderr);
    }

    /// <summary>
    /// The smallest thing that can answer POST /api/agent-tasks and keep what it was sent.
    /// HttpListener rather than a test host: the point is to exercise the SCRIPT, over real HTTP,
    /// exactly as an agent runs it.
    /// </summary>
    private sealed class StubApi : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pump;
        private readonly string _agentKind;
        private readonly Mode _mode;

        public enum Mode { Create, WorktreeHealth, WorktreeHealthEmpty }

        public StubApi(string agentKind = "ClaudeCode", Mode mode = Mode.Create)
        {
            _agentKind = agentKind;
            _mode = mode;
            BaseUrl = EphemeralHttpListener.BindLoopback(_listener);
            _pump = Task.Run(PumpAsync);
        }

        public string BaseUrl { get; }

        public JsonDocument? LastBody { get; private set; }

        public string? LastPath { get; private set; }

        public string? LastMethod { get; private set; }

        public int RequestCount { get; private set; }

        private async Task PumpAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; /* stopped */ }

                RequestCount++;
                LastPath = context.Request.Url?.PathAndQuery;
                LastMethod = context.Request.HttpMethod;
                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                {
                    var raw = await reader.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(raw)) LastBody = JsonDocument.Parse(raw);
                }

                byte[] payload;
                if (_mode == Mode.WorktreeHealth)
                {
                    payload = Encoding.UTF8.GetBytes(
                        """
                        {"findingCount":1,"findings":[{"id":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                         "repoPath":"C:\\repo","branch":"feat/card-task-aabbccdd","path":"C:\\trees\\gone",
                         "taskId":"aabbccdd-0000-0000-0000-000000000001","shortId":"aabbccdd",
                         "shape":"LockedMissing","detail":"locked initializing; directory gone",
                         "severity":"Error","firstSeenAt":"2026-09-03T12:00:00Z",
                         "lastSeenAt":"2026-09-03T12:00:00Z"}]}
                        """);
                    context.Response.StatusCode = 200;
                }
                else if (_mode == Mode.WorktreeHealthEmpty)
                {
                    payload = Encoding.UTF8.GetBytes("""{"findingCount":0,"findings":[]}""");
                    context.Response.StatusCode = 200;
                }
                else
                {
                    payload = Encoding.UTF8.GetBytes(
                        $$"""
                        {"id":"11111111-1111-1111-1111-111111111111","shortId":"11111111",
                         "status":"Queued","modelLevel":"High","warning":null,"agentKind":"{{_agentKind}}"}
                        """);
                    context.Response.StatusCode = 201;
                }

                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(payload);
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch (Exception) { /* already stopped */ }
            try { _pump.Wait(TimeSpan.FromSeconds(5)); } catch (Exception) { /* pump is best-effort */ }
            _listener.Close();
            LastBody?.Dispose();
            _cts.Dispose();
        }
    }
}
