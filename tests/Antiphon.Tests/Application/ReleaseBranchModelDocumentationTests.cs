using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1311 S1. The owner doc and the five pointers name the long-lived rc line.
/// </summary>
[Category("Unit")]
public sealed class ReleaseBranchModelDocumentationTests
{
    private static string Read(params string[] relative) =>
        File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, Path.Combine(relative)));

    [Test]
    public void C1311_ReleaseGatesNameTheLongLivedRcBranch()
    {
        var text = Read("docs", "release-gates.md");
        text.ShouldContain("rc/YYYY-MM-DD", Case.Sensitive);
        text.ShouldContain("vYYYY.MM.DD.N-rc.K", Case.Sensitive);
        text.ShouldContain("merge commit", Case.Sensitive);
        text.ShouldContain("rc tip", Case.Sensitive);
        text.IndexOf(
            "S3 publishes the long-lived pre-release tag and promote-release.ps1 writes the final tag at that peeled SHA; S4 has not changed moving-tip authority yet.",
            StringComparison.Ordinal).ShouldBe(-1);
        text.IndexOf("The candidate ref is exactly", StringComparison.Ordinal).ShouldBe(-1);
        text.IndexOf("S2-S4 have not changed the scripts yet.", StringComparison.Ordinal).ShouldBe(-1);
    }

    [Test]
    public void C1311_ReleaseBranchPointers()
    {
        foreach (var relative in new[]
        {
            new[] { "AGENTS.md" },
            new[] { "docs", "orchestration-loop.md" },
            new[] { "docs", "docker-stack.md" },
            new[] { ".claude", "skills", "antiphon-orchestrator", "SKILL.md" },
            new[] { "scripts", "windmill", "README.md" },
        })
            Read(relative).ShouldContain("rc/YYYY-MM-DD", Case.Sensitive, string.Join('/', relative));
    }
}
