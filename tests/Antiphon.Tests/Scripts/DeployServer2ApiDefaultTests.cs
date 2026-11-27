using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1777. deploy-server2 requires ANTIPHON_API and does not read the operator token, or reach server2, when that name is unset.
/// check-census still skips the token. The value is never printed. Unit 65: the command in process on Server2RollingWorld (the
/// script ran under pwsh with a #!/bin/sh docker and ssh on PATH and a loopback listener, so on Linux only); the world records every
/// read URL and every host action.
/// </summary>
[Category("Unit")]
public sealed class DeployServer2ApiDefaultTests
{
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string TokenSentinel = "SENTINEL_C1777_OPERATOR_TOKEN";
    private const string Api = "http://127.0.0.1:43217";

    [Test]
    public void Unset_refuses_before_token_read_or_docker()
    {
        foreach (var phase in new[] { "deploy-temp", "check-census" })
        {
            AssertRefused(Run(phase, api: null, token: false), phase);
            AssertRefused(Run(phase, api: "   ", token: false), phase);
        }
    }

    [Test]
    public void Set_api_value_is_used_for_the_census_read()
    {
        var run = Run("check-census", Api, token: true);
        run.Urls.ShouldBe(new[] { Api + "/api/projects?includeArchived=true" }, run.Output);
        run.Output.ShouldNotContain(Api, Case.Sensitive);
        run.Output.ShouldNotContain(TokenSentinel, Case.Sensitive);
        run.Output.ShouldNotContain("OperatorTokenMissing", Case.Sensitive);
        run.Output.ShouldNotContain("localhost:17202", Case.Sensitive);
        run.HostActions.ShouldBeEmpty(run.Output);
    }

    [Test]
    public void Check_census_does_not_read_the_operator_token()
    {
        var run = Run("check-census", Api, token: false);
        run.Urls.ShouldBe(new[] { Api + "/api/projects?includeArchived=true" }, run.Output);
        run.Exit.ShouldBe(2, run.Output);
        run.Output.ShouldNotContain("OperatorTokenMissing", Case.Sensitive);
        run.Output.ShouldNotContain("localhost:17202", Case.Sensitive);
        run.HostActions.ShouldBeEmpty(run.Output);
    }

    private static void AssertRefused(Outcome run, string phase)
    {
        run.Exit.ShouldBe(2, phase + " " + run.Output);
        run.Output.ShouldContain("ANTIPHON_API is required", Case.Sensitive, phase);
        run.Output.ShouldContain("docs/docker-stack.md", Case.Sensitive, phase);
        run.Output.ShouldNotContain("OperatorTokenMissing", Case.Sensitive, phase);
        run.Output.ShouldNotContain("localhost:17202", Case.Sensitive, phase);
        run.Output.ShouldNotContain(TokenSentinel, Case.Sensitive, phase);
        run.Urls.ShouldBeEmpty(phase);
        run.HostActions.ShouldBeEmpty(phase + " " + run.Output);
    }

    private static Outcome Run(string phase, string? api, bool token)
    {
        using var world = new Server2RollingWorld(new System.Text.Json.Nodes.JsonObject { ["sha"] = Sha, ["projects"] = new System.Text.Json.Nodes.JsonArray() });
        if (token) File.WriteAllText(world.TokenPath, TokenSentinel);
        else File.Delete(world.TokenPath);
        world.Variables["ANTIPHON_API"] = api;
        world.Variables["ANTIPHON_TASK_TOKEN"] = null;
        var urls = new List<string>();
        world.UrlObserver = urls.Add;
        var run = world.Run("--rolling", "--sha", Sha, "--phase", phase);
        var actions = run.Trace.Where(t => t["kind"]?.GetValue<string>() != "http").Select(t => t.ToJsonString()).ToArray();
        return new Outcome(run.Exit, run.Output, urls.ToArray(), actions);
    }

    private sealed record Outcome(int Exit, string Output, string[] Urls, string[] HostActions);
}
