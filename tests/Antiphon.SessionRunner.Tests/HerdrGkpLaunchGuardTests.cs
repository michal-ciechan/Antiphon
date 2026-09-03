using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0341: a gkp (local llm-key-proxy) Grok launch is refused unless the launch env can route
/// it — a resolvable project, <c>GROK_BASE_URL</c>, and a dummy key. Keyed on <c>gkp.ps1</c> in
/// the arguments, so the pool <c>grok.exe</c> path (#28) never trips it.
/// </summary>
public class HerdrGkpLaunchGuardTests
{
    private static readonly string[] GkpArgs =
    [
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
        @"C:\Users\x\.local\bin\gkp.ps1", "--project", "$env:X_LLM_PROJECT",
    ];

    private static Dictionary<string, string> RoutingEnv(params string[] omit)
    {
        var env = new Dictionary<string, string>
        {
            ["X_LLM_PROJECT"] = "PredictionMarkets",
            ["GROK_BASE_URL"] = "http://localhost:10746/v1",
            ["XAI_API_KEY"] = "llm-key-proxy",
            ["GROK_CLI_CHAT_PROXY_BASE_URL"] = "http://localhost:10746/v1",
        };
        foreach (var name in omit)
            env.Remove(name);
        return env;
    }

    [Test]
    public void Is_gkp_launch_keys_on_the_wrapper_file_name_only()
    {
        HerdrGkpLaunchGuard.IsGkpLaunch(GkpArgs).ShouldBeTrue();
        HerdrGkpLaunchGuard.IsGkpLaunch(["-File", "GKP.PS1"]).ShouldBeTrue();
        HerdrGkpLaunchGuard.IsGkpLaunch(["--always-approve", "--no-alt-screen"]).ShouldBeFalse();
        HerdrGkpLaunchGuard.IsGkpLaunch([@"C:\tools\gkp.ps1.bak"]).ShouldBeFalse();
        HerdrGkpLaunchGuard.IsGkpLaunch(null).ShouldBeFalse();
        HerdrGkpLaunchGuard.IsGkpLaunch([]).ShouldBeFalse();
    }

    [Test]
    public void Fully_routed_env_has_nothing_missing()
    {
        HerdrGkpLaunchGuard.MissingRequirements(GkpArgs, RoutingEnv()).ShouldBeEmpty();
        Should.NotThrow(() => HerdrGkpLaunchGuard.Require(Guid.NewGuid(), GkpArgs, RoutingEnv(), NullLogger.Instance));
    }

    [Test]
    public void Each_missing_routing_fact_is_named()
    {
        var noProject = HerdrGkpLaunchGuard.MissingRequirements(GkpArgs, RoutingEnv("X_LLM_PROJECT"));
        noProject.ShouldHaveSingleItem().ShouldContain("X_LLM_PROJECT");

        var noUrl = HerdrGkpLaunchGuard.MissingRequirements(GkpArgs, RoutingEnv("GROK_BASE_URL"));
        noUrl.ShouldBe(["GROK_BASE_URL"]);

        var noKey = HerdrGkpLaunchGuard.MissingRequirements(GkpArgs, RoutingEnv("XAI_API_KEY"));
        noKey.ShouldHaveSingleItem().ShouldContain("XAI_API_KEY");

        HerdrGkpLaunchGuard.MissingRequirements(GkpArgs, new Dictionary<string, string>()).Count.ShouldBe(3);
        HerdrGkpLaunchGuard.MissingRequirements(GkpArgs, null).Count.ShouldBe(3);
    }

    [Test]
    public void Alternate_key_name_and_case_insensitive_names_satisfy_the_gate()
    {
        var env = RoutingEnv("XAI_API_KEY");
        env["GROK_CODE_XAI_API_KEY"] = "llm-key-proxy";
        HerdrGkpLaunchGuard.MissingRequirements(GkpArgs, env).ShouldBeEmpty();

        var lower = new Dictionary<string, string>
        {
            ["x_llm_project"] = "PM",
            ["grok_base_url"] = "http://localhost:10746/v1",
            ["xai_api_key"] = "k",
        };
        HerdrGkpLaunchGuard.MissingRequirements(GkpArgs, lower).ShouldBeEmpty();
    }

    [Test]
    public void Blank_values_do_not_count()
    {
        var env = RoutingEnv();
        env["GROK_BASE_URL"] = "  ";
        HerdrGkpLaunchGuard.MissingRequirements(GkpArgs, env).ShouldBe(["GROK_BASE_URL"]);
    }

    [Test]
    public void A_literal_project_argument_satisfies_the_project_requirement_without_the_marker()
    {
        var env = RoutingEnv("X_LLM_PROJECT");
        string[] literal = ["-File", @"C:\x\gkp.ps1", "--project", "PredictionMarkets"];
        HerdrGkpLaunchGuard.MissingRequirements(literal, env).ShouldBeEmpty();

        string[] equalsForm = ["-File", @"C:\x\gkp.ps1", "--project=PredictionMarkets"];
        HerdrGkpLaunchGuard.MissingRequirements(equalsForm, env).ShouldBeEmpty();

        // An unresolvable $env: token is what PowerShell would pass verbatim — not a project.
        HerdrGkpLaunchGuard.MissingRequirements(GkpArgs, env).ShouldHaveSingleItem().ShouldContain("project");

        // A token naming a different, present variable resolves.
        string[] other = ["-File", @"C:\x\gkp.ps1", "--project", "$env:OTHER_PROJECT"];
        env["OTHER_PROJECT"] = "Atlas";
        HerdrGkpLaunchGuard.MissingRequirements(other, env).ShouldBeEmpty();

        // A blank literal is not a project either.
        string[] blank = ["-File", @"C:\x\gkp.ps1", "--project", ""];
        HerdrGkpLaunchGuard.MissingRequirements(blank, RoutingEnv("X_LLM_PROJECT")).ShouldHaveSingleItem();

        // No --project at all and no marker: missing.
        string[] none = ["-File", @"C:\x\gkp.ps1"];
        HerdrGkpLaunchGuard.MissingRequirements(none, RoutingEnv("X_LLM_PROJECT")).ShouldHaveSingleItem();
    }

    [Test]
    public void TryReadProjectArgument_reads_both_forms_and_the_last_one_wins()
    {
        HerdrGkpLaunchGuard.TryReadProjectArgument(["--project", "A"], out var v).ShouldBeTrue();
        v.ShouldBe("A");
        HerdrGkpLaunchGuard.TryReadProjectArgument(["--project=B"], out v).ShouldBeTrue();
        v.ShouldBe("B");
        HerdrGkpLaunchGuard.TryReadProjectArgument(["--project", "A", "--project=B"], out v).ShouldBeTrue();
        v.ShouldBe("B");
        HerdrGkpLaunchGuard.TryReadProjectArgument(["--project"], out _).ShouldBeFalse();
        HerdrGkpLaunchGuard.TryReadProjectArgument(["--projects", "x"], out _).ShouldBeFalse();
    }

    [Test]
    public void Require_throws_the_gkp_code_naming_the_session_and_the_missing_facts()
    {
        var sessionId = Guid.NewGuid();
        var ex = Should.Throw<HerdrLaunchException>(() =>
            HerdrGkpLaunchGuard.Require(sessionId, GkpArgs, new Dictionary<string, string>(), NullLogger.Instance));
        ex.Code.ShouldBe(HerdrProblemTypes.GkpEnvMissing);
        ex.Message.ShouldContain(sessionId.ToString("D"));
        ex.Message.ShouldContain("X_LLM_PROJECT");
        ex.Message.ShouldContain("GROK_BASE_URL");
        ex.Message.ShouldContain("XAI_API_KEY");
        ex.Message.ShouldContain("grok.com");
        ex.Message.ShouldContain("DefaultLaunchEnv");
    }

    [Test]
    public void Require_never_fires_for_a_bare_grok_exe_launch()
    {
        // #28's pool path: registry grok.exe, no gkp — a different mechanism, out of scope here.
        Should.NotThrow(() => HerdrGkpLaunchGuard.Require(
            Guid.NewGuid(), ["--always-approve", "--no-alt-screen"], new Dictionary<string, string>(), NullLogger.Instance));
        Should.NotThrow(() => HerdrGkpLaunchGuard.Require(
            Guid.NewGuid(), [], null, NullLogger.Instance));
    }

    [Test]
    public void Missing_chat_proxy_url_warns_but_does_not_refuse()
    {
        var logs = new List<string>();
        var env = RoutingEnv("GROK_CLI_CHAT_PROXY_BASE_URL");
        Should.NotThrow(() => HerdrGkpLaunchGuard.Require(
            Guid.NewGuid(), GkpArgs, env, new ListLogger<HerdrGkpLaunchGuardTests>(logs)));
        logs.ShouldContain(l =>
            l.Contains("[Warning]", StringComparison.Ordinal)
            && l.Contains("GROK_CLI_CHAT_PROXY_BASE_URL", StringComparison.Ordinal));

        logs.Clear();
        Should.NotThrow(() => HerdrGkpLaunchGuard.Require(
            Guid.NewGuid(), GkpArgs, RoutingEnv(), new ListLogger<HerdrGkpLaunchGuardTests>(logs)));
        logs.ShouldBeEmpty();
    }
}
