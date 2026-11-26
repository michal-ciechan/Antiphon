using Antiphon.Tests.Application;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Invariants;

/// <summary>
/// CARD-1311 S1. The owner doc and the five pointers name the long-lived rc line.
/// </summary>
[Category("RepositoryInvariant")]
[Category("Unit")]
public sealed class ReleaseBranchModelDocumentationTests
{
    private const string ReleaseGatesPath = "docs/release-gates.md";

    private static string Read(params string[] relative) =>
        File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, Path.Combine(relative)));

    [Test]
    public void C1311_ReleaseGatesNameTheLongLivedRcBranch()
    {
        var text = Read(ReleaseGatesPath);
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

    [Test]
    public void C1199_FinalClosureDocumentation()
    {
        var text = Read(ReleaseGatesPath);
        text.ShouldContain("The original CARD-1199 census had 35 checkpoint identities and zero runtime skips", Case.Sensitive,
            "c1199-doc-runtime-closure: the original roster had no runtime skips after S-C2");
        text.ShouldContain("All 37 checkpoint identities are required on both platforms with no policy exclusions.", Case.Sensitive,
            "c1199-doc-current-roster: the two portable CARD-2006 cases are required on both platforms");
        text.ShouldContain("Native Windows verification remains owed", Case.Sensitive,
            "c1199-doc-native-incomplete: gate removal is not a native execution receipt");
        text.ShouldContain("S-F-cost requires S3 admission on its target base", Case.Sensitive,
            "c1199-doc-cost-prerequisite: successful native class costs are still required");
        text.ShouldContain("R-2 junction and custody qualification stays pending/required", Case.Sensitive);
        text.ShouldNotContain("Ten shape mutations, eight tracked-baseline cases, and five\nconfinement cases stay runtime-skipped", Case.Sensitive);
    }
}
