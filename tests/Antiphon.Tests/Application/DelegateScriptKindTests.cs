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
/// CARD-0084 S2's caller-facing half: <c>scripts/antiphon delegate create --kind</c> (Antiphon.Nightly.DelegateCli,
/// which replaced scripts/delegate.ps1 in unit 28 of the 2026-11-22 plan). The command is what an agent actually runs, so
/// these drive it in process over real HTTP against a stub server and assert on the JSON body it posts — a string-match on the source would pass just as happily if the flag
/// never reached the request.
///
/// <para>The one thing that must not regress is the omitted case: a caller who never heard of
/// --kind must produce byte-for-byte the request they produced before the flag existed, because the
/// server would then resolve a kind from the role policy that the caller did not choose.</para>
/// </summary>
[Category("Integration")]
public sealed class DelegateScriptKindTests
{
    [Test]
    [Arguments("omitted")]
    [Arguments("base_task")]
    [Arguments("fresh")]
    [Arguments("both_flags")]
    [Arguments("shared")]
    [Arguments("readonly")]
    [Arguments("onagent_task")]
    [Arguments("onagent_fresh")]
    [Arguments("ambiguous_response")]
    public async Task T0442_V16_worktree_base_payload_and_validation(string mode)
    {
        using var server = mode == "ambiguous_response"
            ? new StubApi(createJson:
                """{"title":"Conflict","status":409,"code":"worktree_base_ambiguous","detail":"worktree_base_ambiguous: tasks aaaaaaaa on feat/card-task-aaaaaaaa @ aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa and bbbbbbbb on feat/card-task-bbbbbbbb @ bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb; choose --base-task or --fresh-worktree."}""",
                createStatusCode: 409)
            : new StubApi();
        var args = new List<string> { "create", "--role", "Code", "--goal", "build it", "--card", "CARD-0442" };
        switch (mode)
        {
            case "base_task": args.AddRange(["--worktree", "--base-task", "1234abcd"]); break;
            case "fresh": args.AddRange(["--worktree", "--fresh-worktree"]); break;
            case "both_flags": args.AddRange(["--base-task", "1234abcd", "--fresh-worktree"]); break;
            case "shared": args.AddRange(["--shared", "--base-task", "1234abcd"]); break;
            case "readonly": args.AddRange(["--read-only", "--fresh-worktree"]); break;
            case "onagent_task": args.AddRange(["--on-agent", "1234abcd", "--base-task", "1234abcd"]); break;
            case "onagent_fresh": args.AddRange(["--on-agent", "1234abcd", "--fresh-worktree"]); break;
            case "ambiguous_response": args.Add("--worktree"); break;
        }

        var run = await RunDelegateAsync(server, [.. args]);
        var invalid = mode is "both_flags" or "shared" or "readonly" or "onagent_task" or "onagent_fresh";
        if (invalid)
        {
            run.ExitCode.ShouldNotBe(0);
            server.RequestCount.ShouldBe(0);
            return;
        }
        if (mode == "ambiguous_response")
        {
            run.ExitCode.ShouldNotBe(0);
            server.RequestCount.ShouldBe(1);
            run.Output.ShouldContain("worktree_base_ambiguous");
            run.Output.ShouldContain("aaaaaaaa");
            run.Output.ShouldContain("bbbbbbbb");
            run.Output.ShouldContain("--base-task");
            run.Output.ShouldContain("--fresh-worktree");
            return;
        }
        run.ExitCode.ShouldBe(0, run.Output);
        server.RequestCount.ShouldBe(1);
        var body = server.LastBody!.RootElement;
        var hasBase = body.TryGetProperty("worktreeBaseTask", out var baseTask);
        var hasFresh = body.TryGetProperty("freshWorktree", out var fresh);
        hasBase.ShouldBe(mode == "base_task");
        hasFresh.ShouldBe(mode == "fresh");
        if (hasBase) baseTask.GetString().ShouldBe("1234abcd");
        if (hasFresh) fresh.GetBoolean().ShouldBeTrue();
        if (mode == "base_task")
        {
            body.GetProperty("workspace").GetString().ShouldBe("Worktree");
            var workspaceRun = await RunDelegateAsync(server, "create", "--role", "Code", "--goal", "build it",
                "--card", "CARD-0442", "--workspace", "Worktree", "--base-task", "1234abcd");
            workspaceRun.ExitCode.ShouldBe(0, workspaceRun.Output);
            server.RequestCount.ShouldBe(2);
            server.LastBody!.RootElement.GetProperty("workspace").GetString().ShouldBe("Worktree");
            server.LastBody.RootElement.GetProperty("worktreeBaseTask").GetString().ShouldBe("1234abcd");
        }
    }

    [Test]
    public async Task Kind_Grok_is_posted_as_agentKind()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "create", "--role", "Test", "--goal", "run the suite", "--kind", "Grok");

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
        var run = await RunDelegateAsync(server, "create", "--role", "Test", "--goal", "run the suite");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.TryGetProperty("agentKind", out _)
            .ShouldBeFalse("an omitted -Kind must leave the request exactly as it was before the flag existed");
    }

    [Test]
    public async Task Model_is_posted_only_when_set()
    {
        using var omitted = new StubApi();
        var plain = await RunDelegateAsync(
            omitted, "create", "--role", "Test", "--goal", "run the suite", "--kind", "Codex", "--level", "High");
        plain.ExitCode.ShouldBe(0, plain.Output);
        omitted.LastBody.ShouldNotBeNull().RootElement.TryGetProperty("model", out _)
            .ShouldBeFalse("an omitted -Model must not appear on the create body");

        using var chosen = new StubApi();
        var explicitModel = await RunDelegateAsync(
            chosen, "create", "--role", "Test", "--goal", "run the suite", "--kind", "Codex", "--level", "High",
            "--model", "gpt-6.2-astra");
        explicitModel.ExitCode.ShouldBe(0, explicitModel.Output);
        chosen.LastBody.ShouldNotBeNull().RootElement.GetProperty("model").GetString()
            .ShouldBe("gpt-6.2-astra");
    }

    [Test]
    public async Task Kind_Codex_is_posted_as_agentKind()
    {
        // CARD-0099 S3. Until this slice, Codex was the value this file used as its EXAMPLE of a
        // refused kind — the ValidateSet is the caller-facing half of the allowlist, so widening one
        // without the other leaves the flag rejected at the prompt no matter what the server admits.
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "create", "--role", "Test", "--goal", "run the suite", "--kind", "Codex");

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
        var run = await RunDelegateAsync(server, "create", "--role", "Test", "--goal", "run the suite", "--kind", "OpenCode");

        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldContain("OpenCode");
        server.RequestCount.ShouldBe(0, "a rejected flag must not reach the server");
    }

    [Test]
    public void the_commands_kind_choices_are_exactly_the_servers_allowlist()
    {
        // The two halves are separate projects, and CARD-0099 is the second time they had to move
        // together. A choice list narrower than the server silently makes a kind undispatchable; one
        // wider than it turns a clean refusal into a 422 round trip.
        var kinds = Antiphon.Nightly.DelegateCliOptions.Kinds;

        foreach (var kind in AgentTaskService.DelegatableKinds)
            kinds.ShouldContain(kind.ToString());
        foreach (var kind in Enum.GetValues<AgentKind>())
        {
            if (AgentTaskService.DelegatableKinds.Contains(kind))
                continue;
            kinds.ShouldNotContain(kind.ToString());
        }
    }

    [Test]
    public async Task the_resolved_kind_is_echoed_back_to_the_caller()
    {
        // The caller may have chosen nothing and still be running on Grok (a role promoted in
        // config), so the ECHO comes from the server's answer, not from the flag.
        using var server = new StubApi(agentKind: "Grok");
        var run = await RunDelegateAsync(server, "create", "--role", "Code", "--goal", "write it");

        run.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldContain("Grok");
    }

    [Test]
    public async Task a_ClaudeCode_task_is_announced_exactly_as_it_always_was()
    {
        using var server = new StubApi(agentKind: "ClaudeCode");
        var run = await RunDelegateAsync(server, "create", "--role", "Test", "--goal", "run the suite");

        run.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldContain("queued task");
        run.Output.ShouldNotContain("ClaudeCode", customMessage: "the default kind is not news");
    }

    [Test]
    public async Task Complexity_Hard_is_posted_as_complexity()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(
            server, "create", "--role", "Plan", "--goal", "plan it", "--complexity", "Hard");

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
        var run = await RunDelegateAsync(server, "create", "--role", "Plan", "--goal", "plan it");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.TryGetProperty("complexity", out _)
            .ShouldBeFalse("an omitted -Complexity must leave the request exactly as it was before the flag existed");
    }

    [Test]
    public async Task Complexity_routed_line_names_the_cell_and_via_any_role()
    {
        using var server = new StubApi(createJson: """
            {"id":"11111111-1111-1111-1111-111111111111","shortId":"11111111",
             "status":"Queued","modelLevel":"Frontier","warning":null,"agentKind":"Grok",
             "complexity":"Hard",
             "routing":{"complexity":"Hard","chainProvenance":"Human","chainSource":"pin",
               "source":"chain:Hard","role":"Plan","chainRole":null,"walked":true,
               "available":["grok-4.7"],
               "candidates":[
                 {"agentKind":"ClaudeCode","modelLevel":"Frontier","alias":"fable","outcome":"skipped","reason":"held","origin":"chain"},
                 {"agentKind":"Grok","modelLevel":"Frontier","alias":"grok-4.7","outcome":"chosen","reason":null,"origin":"chain"}
               ]}}
            """);
        var run = await RunDelegateAsync(
            server, "create", "--role", "Plan", "--goal", "plan it", "--complexity", "Hard");

        run.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldContain("routed Plan/Hard -> grok-4.7 (candidate 2/2; via any-role Hard chain)");
        run.Output.ShouldContain("skipped fable");
    }

    [Test]
    public async Task Complexity_routed_line_names_a_role_cell_without_via()
    {
        using var server = new StubApi(createJson: """
            {"id":"11111111-1111-1111-1111-111111111111","shortId":"11111111",
             "status":"Queued","modelLevel":"Frontier","warning":null,"agentKind":"Grok",
             "complexity":"Hard",
             "routing":{"complexity":"Hard","chainProvenance":"Human","chainSource":"pin",
               "source":"chain:Plan/Hard","role":"Plan","chainRole":"Plan","walked":true,
               "available":["grok-4.7"],
               "candidates":[
                 {"agentKind":"Grok","modelLevel":"Frontier","alias":"grok-4.7","outcome":"chosen","reason":null,"origin":"chain"}
               ]}}
            """);
        var run = await RunDelegateAsync(
            server, "create", "--role", "Plan", "--goal", "plan it", "--complexity", "Hard");

        run.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldContain("routed Plan/Hard -> grok-4.7 (candidate 1/1)");
        run.Output.ShouldNotContain("via any-role");
    }

    [Test]
    public async Task Complexity_plus_Kind_is_refused_locally()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(
            server, "create", "--role", "Plan", "--goal", "plan it", "--complexity", "Hard", "--kind", "Grok");

        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldContain("never silently");
        run.Output.ShouldContain("rerouted");
        server.RequestCount.ShouldBe(0);
    }

    [Test]
    public async Task Reroute_posts_kind_and_level()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(
            server, "reroute", "11111111-1111-1111-1111-111111111111",
            "--kind", "Grok", "--level", "Frontier");

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
            server, "create", "--role", "Plan", "--goal", "plan it", "--complexity", "Easy", "--refuse-if-exhausted");

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
            server, "create", "--role", "Code", "--goal", "do the remaining epics",
            "--authority", "start the remaining Coesite downloader epics one after another");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("authority").GetString()
            .ShouldBe("start the remaining Coesite downloader epics one after another");
    }

    [Test]
    public async Task an_omitted_Authority_sends_nothing_at_all()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "create", "--role", "Code", "--goal", "do the remaining epics");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.TryGetProperty("authority", out _)
            .ShouldBeFalse("an omitted -Authority must leave the request exactly as it was before the flag existed");
    }

    [Test]
    public async Task Continue_posts_to_continue_and_prints_the_success_line()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "continue", "1234abcd");

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
            server, "create", "--role", "Test", "--goal", "run anyway", "--ignore-subscription-quota");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("ignoreSubscriptionQuota").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task EnvOverride_is_posted_as_launchEnvOverride()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "create", "--role", "Test", "--goal", "run the suite",
            "--env-override", "ANTHROPIC_BASE_URL=http://proxy:8080,ANTHROPIC_API_KEY={{key:proxy-key}}");

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
        var run = await RunDelegateAsync(server, "create", "--role", "Test", "--goal", "run the suite");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.TryGetProperty("launchEnvOverride", out _)
            .ShouldBeFalse("an omitted --env-override must leave the request exactly as it was before the flag existed");
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
            "create", "--role", "Test", "--goal", "run the suite");

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
            "create", "--role", "Test", "--goal", "run the suite", "--no-inherit-env");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.TryGetProperty("inheritedLlmEnv", out _).ShouldBeFalse();
    }

    [Test]
    public async Task EnvOverride_keeps_its_own_LLM_project_out_of_inheritedLlmEnv()
    {
        using var server = new StubApi();
        var run = await DelegateScriptRunner.RunAsync(
            server.BaseUrl,
            new Dictionary<string, string?>
            {
                ["X_LLM_PROJECT"] = "from-shell",
                ["X_LLM_KEY"] = "pm-key",
            },
            "create", "--role", "Test", "--goal", "run the suite", "--env-override", "X_LLM_PROJECT=override");

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
        var run = await RunDelegateAsync(server, "create", "--role", "Test", "--goal", "run the suite");

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
            server, "create", "--role", "Code", "--kind", "Grok", "--goal", "queue anyway",
            "--allow-unauthenticated-provider");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("allowUnauthenticatedProvider").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task an_omitted_AllowUnauthenticatedProvider_sends_no_flag()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "create", "--role", "Test", "--goal", "run the suite");

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
            server, "create", "--role", "Test", "--goal", "queue anyway", "--ignore-model-disabled");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("ignoreModelDisabled").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task an_omitted_IgnoreModelDisabled_sends_no_ignoreModelDisabled_at_all()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "create", "--role", "Test", "--goal", "run the suite");

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
            server, "create", "--role", "Test", "--goal", "run in parallel", "--ignore-concurrency-limit");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("ignoreConcurrencyLimit").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task an_omitted_IgnoreConcurrencyLimit_sends_no_ignoreConcurrencyLimit_at_all()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "create", "--role", "Test", "--goal", "run the suite");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.TryGetProperty("ignoreConcurrencyLimit", out _)
            .ShouldBeFalse("the flag is sent only when chosen, matching -IgnoreSubscriptionQuota");
    }

    [Test]
    public async Task WorktreeHealth_posts_and_prints_findings_without_pruning()
    {
        using var server = new StubApi(mode: StubApi.Mode.WorktreeHealth);
        var run = await RunDelegateAsync(server, "worktree-health");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastPath.ShouldBe("/api/agent-tasks/worktree-health");
        server.LastMethod.ShouldBe("POST");
        run.Output.ShouldContain("feat/card-task-aabbccdd");
        run.Output.ShouldContain("detection only");
        run.Output.ShouldContain("nothing pruned");
        run.Output.ShouldNotContain("worktree remove");
    }

    [Test]
    public async Task Stage_is_posted_as_stage()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(
            server, "create", "--role", "Debug", "--goal", "verify the diagnosis", "--stage", "Verify");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("stage").GetString().ShouldBe("Verify");
    }

    [Test]
    public async Task an_omitted_Stage_sends_no_stage_at_all()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "create", "--role", "Review", "--goal", "judge it");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.TryGetProperty("stage", out _)
            .ShouldBeFalse("an omitted -Stage must leave the role default to the server");
    }

    [Test]
    public async Task an_unknown_Stage_is_refused_by_the_script_before_any_request()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(
            server, "create", "--role", "Debug", "--goal", "verify", "--stage", "PlanReview");

        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldContain("PlanReview");
        server.RequestCount.ShouldBe(0, "a rejected flag must not reach the server");
    }

    [Test]
    public async Task Finding_Found_posts_stage_and_detail()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(
            server, "finding", "abcd1234", "--stage", "Review", "--found", "hole in X");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastMethod.ShouldBe("POST");
        server.LastPath.ShouldBe("/api/agent-tasks/abcd1234/finding");
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("stage").GetString().ShouldBe("Review");
        body.RootElement.GetProperty("found").GetBoolean().ShouldBeTrue();
        body.RootElement.GetProperty("detail").GetString().ShouldBe("hole in X");
        run.Output.ShouldContain("recorded Review found on task abcd1234");
    }

    [Test]
    public async Task Finding_Clean_posts_found_false()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(
            server, "finding", "abcd1234", "--stage", "Review", "--clean");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("found").GetBoolean().ShouldBeFalse();
        body.RootElement.TryGetProperty("detail", out _).ShouldBeFalse();
        run.Output.ShouldContain("recorded Review clean on task abcd1234");
    }

    [Test]
    public async Task WorktreeHealth_with_no_findings_says_so()
    {
        using var server = new StubApi(mode: StubApi.Mode.WorktreeHealthEmpty);
        var run = await RunDelegateAsync(server, "worktree-health");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastPath.ShouldBe("/api/agent-tasks/worktree-health");
        run.Output.ShouldContain("No stuck delegate worktrees.");
    }

    // ---- harness -------------------------------------------------------------------------------

    // The runner itself lives in DelegateScriptRunner so S6's end-to-end test invokes the
    // delegate command exactly the way this one does - same parse, same transports, same environment.
    private static Task<(int ExitCode, string Output)> RunDelegateAsync(StubApi server, params string[] args) =>
        DelegateScriptRunner.RunAsync(server.BaseUrl, args);

    /// <summary>
    /// The smallest thing that can answer POST /api/agent-tasks and keep what it was sent.
    /// HttpListener rather than a test host: the point is to exercise the command over real HTTP,
    /// exactly as an agent runs it.
    /// </summary>
    private sealed class StubApi : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pump;
        private readonly string _agentKind;
        private readonly Mode _mode;
        private readonly string? _createJson;
        private readonly int _createStatusCode;

        public enum Mode { Create, WorktreeHealth, WorktreeHealthEmpty }

        public StubApi(string agentKind = "ClaudeCode", Mode mode = Mode.Create,
            string? createJson = null, int createStatusCode = 201)
        {
            _agentKind = agentKind;
            _mode = mode;
            _createJson = createJson;
            _createStatusCode = createStatusCode;
            BaseUrl = EphemeralHttpListener.BindLoopback(out _listener);
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
                        _createJson
                        ?? $$"""
                        {"id":"11111111-1111-1111-1111-111111111111","shortId":"11111111",
                         "status":"Queued","modelLevel":"High","warning":null,"agentKind":"{{_agentKind}}"}
                        """);
                    context.Response.StatusCode = _createStatusCode;
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
