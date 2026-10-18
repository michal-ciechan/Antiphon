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
        text.ShouldContain(
            "S2 accepts the long-lived rc ref; S3-S4 have not changed pre-release publication or moving-tip authority yet.",
            Case.Sensitive);
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
