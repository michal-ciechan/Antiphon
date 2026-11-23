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

    // The hold command is keep-six (Antiphon.Nightly.KeepSix, which replaced scripts/keep-six.ps1's body in unit 23 of the
    // 2026-11-22 plan; the .ps1 is the cutover's thin launcher until W8); KeepSixCommandTests runs its routes, headers and refusals.
    [Test]
    public void bundle_says_the_server_does_not_dispatch_and_names_the_hold_script()
    {
        var bundle = Read("server", "Bundles", "orchestrator.md");
        bundle.ShouldContain("scripts/antiphon keep-six", Case.Sensitive);
        bundle.ShouldContain("The server does not dispatch", Case.Sensitive);

        var sourcePath = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "KeepSix.cs");
        File.Exists(sourcePath).ShouldBeTrue(sourcePath);
        // scripts/keep-six.ps1 stays only as the cutover's thin launcher into the command (no API code of its own).
        var launcher = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "keep-six.ps1"));
        launcher.ShouldContain("Invoke-AntiphonCli -Command 'keep-six'", Case.Sensitive);
        launcher.ShouldNotContain("Invoke-RestMethod", Case.Sensitive);
        launcher.ShouldNotContain("Invoke-WebRequest", Case.Sensitive);
        var source = File.ReadAllText(sourcePath);
        source.All(static c => c <= 127).ShouldBeTrue("KeepSix.cs must stay ASCII");
        source.ShouldContain("OperatorApi.TaskTokenHeader", Case.Sensitive);
        source.ShouldContain("\"/hold\"", Case.Sensitive);
        source.ShouldContain("\"/ack\"", Case.Sensitive);
        source.ShouldContain("\"--hold\"", Case.Sensitive);
        source.ShouldContain("\"--ack\"", Case.Sensitive);
        source.IndexOf("/api/agent-tasks", StringComparison.Ordinal).ShouldBe(-1);
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
        bullets[0].ShouldContain("scripts/antiphon keep-six", Case.Sensitive);

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
        section.ShouldContain("scripts/antiphon keep-six", Case.Sensitive);
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
