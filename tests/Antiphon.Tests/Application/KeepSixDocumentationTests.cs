using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1320 S4. The bundle and the docs point at the server setting. The server does not dispatch.
/// </summary>
[Category("Unit")]
public sealed class KeepSixDocumentationTests
{
    private static string Read(params string[] relative) =>
        File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, Path.Combine(relative)));

    [Test]
    public void bundle_says_the_server_does_not_dispatch_and_names_the_hold_script()
    {
        var bundle = Read("server", "Bundles", "orchestrator.md");
        bundle.ShouldContain("scripts/keep-six.ps1", Case.Sensitive);
        bundle.ShouldContain("The server does not dispatch", Case.Sensitive);

        var scriptPath = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "keep-six.ps1");
        File.Exists(scriptPath).ShouldBeTrue(scriptPath);
        var script = File.ReadAllText(scriptPath);
        script.All(static c => c <= 127).ShouldBeTrue("keep-six.ps1 must stay ASCII");
        script.ShouldContain("X-Antiphon-Task-Token", Case.Sensitive);
        script.ShouldContain("/keep-six/hold", Case.Sensitive);
        script.ShouldContain("/keep-six/ack", Case.Sensitive);
        script.ShouldContain("-Hold", Case.Sensitive);
        script.ShouldContain("-Ack", Case.Sensitive);
        script.IndexOf("/api/agent-tasks", StringComparison.Ordinal).ShouldBe(-1);
    }

    [Test]
    public void agents_and_orchestration_loop_point_at_the_server_setting()
    {
        var agents = Read("AGENTS.md").Split('\n');
        var bullets = agents.Where(line =>
            line.StartsWith("- ", StringComparison.Ordinal)
            && line.Contains("server setting", StringComparison.Ordinal)
            && line.Contains("The server does not dispatch", StringComparison.Ordinal)
            && !line.StartsWith("- An orchestrator working a board", StringComparison.Ordinal))
            .ToArray();
        bullets.Length.ShouldBe(1);
        bullets[0].ShouldContain("scripts/keep-six.ps1", Case.Sensitive);

        var loop = Read("docs", "orchestration-loop.md");
        var policy = loop.IndexOf("### Standing pipeline policy", StringComparison.Ordinal);
        var retrospective = loop.IndexOf("### Post-land miss retrospective", policy, StringComparison.Ordinal);
        policy.ShouldBeGreaterThanOrEqualTo(0);
        retrospective.ShouldBeGreaterThan(policy);
        var window = loop[policy..retrospective];
        const string heading = "### Keep-six in-flight reminder (CARD-1320)";
        var sectionAt = window.IndexOf(heading, StringComparison.Ordinal);
        sectionAt.ShouldBeGreaterThanOrEqualTo(0);
        var next = window.IndexOf("\n### ", sectionAt + heading.Length, StringComparison.Ordinal);
        var section = next < 0 ? window[sectionAt..] : window[sectionAt..next];
        section.ShouldContain("server setting", Case.Sensitive);
        section.ShouldContain("The server does not dispatch", Case.Sensitive);
        section.ShouldContain("scripts/keep-six.ps1", Case.Sensitive);
    }

    [Test]
    public void bootstrap_and_ops_http_name_the_job_and_the_route()
    {
        Read("docs", "bootstrap.md").ShouldContain("`antiphon:keep-six`", Case.Sensitive);
        var http = Read("docs", "ops-http.md");
        http.ShouldContain("GET /api/orchestrators/keep-six", Case.Sensitive);
        http.ShouldContain("PUT /api/orchestrators/{agentId}/keep-six", Case.Sensitive);
        http.ShouldContain("/api/orchestrators/{agentId}/keep-six/hold", Case.Sensitive);
        http.ShouldContain("POST /api/orchestrators/{agentId}/keep-six/ack", Case.Sensitive);
    }
}
