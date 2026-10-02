using System.Security.Cryptography;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointRepeatDocumentationTests : CheckpointTestBase
{
    [Test]
    public void repeat_budget_reaches_code_briefs_without_bundle_growth()
    {
        var root = CheckpointFixtures.RepoRoot;
        var testing = File.ReadAllText(Path.Combine(root, "docs", "testing-and-build.md"));
        var orchestration = File.ReadAllText(Path.Combine(root, "docs", "orchestration-loop.md"));
        var stage = File.ReadAllBytes(Path.Combine(root, "server", "Bundles", "stage-code.md"));
        var orchestrator = File.ReadAllBytes(Path.Combine(root, "server", "Bundles", "orchestrator.md"));
        const string template = "repeat-proof: at most 3 normal + 2 loaded repetitions per unchanged proof selection; none required after green. Exceed only for a flake already demonstrated by Review; cite that Review, filter, reason and revised budget.";
        testing.ShouldContain(template, Case.Sensitive, "testing-owner-repeat-cap");
        orchestration.ShouldContain(template, Case.Sensitive, "code-brief-repeat-cap");
        System.Text.Encoding.UTF8.GetString(stage).ShouldContain(template, Case.Sensitive, "stage-code-repeat-cap");
        System.Text.Encoding.UTF8.GetString(stage).ShouldContain("CHECKPOINTS:");
        System.Text.Encoding.UTF8.GetString(stage).ShouldContain("SOURCE:");
        System.Text.Encoding.UTF8.GetString(stage).ShouldContain("slot=");
        System.Text.Encoding.UTF8.GetString(stage).ShouldContain("next: review");
        stage.Length.ShouldBeLessThan(2410, "stage-code-net-shorter-than-code-base");
        stage.All(value => value < 128).ShouldBeTrue("stage-code-ascii");
        Convert.ToHexString(SHA256.HashData(orchestrator)).ToLowerInvariant()
            .ShouldBe("62b51e5cc7e20110fe00ed3530ce1d2a5a7400506bd52ffa0a2244f311f50aee", "orchestrator-unchanged");
    }
}
