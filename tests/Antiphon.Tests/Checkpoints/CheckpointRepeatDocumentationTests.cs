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
        // CARD-0884 compresses existing policy to restore real argv headroom.
        // CARD-0479 keeps the binding-only sentence and trims unpinned platform examples
        // so the 500-char command-line headroom still holds. CARD-0822's bundle swap is
        // in the same file, so this pin is the SHA-256 of the merged LF bytes and is
        // recomputed whenever orchestrator.md changes.
        // CARD-0881 swaps the three-route policy for the effective-settings route.
        // Repeat policy remains in Code's bundle and owner docs.
        // CARD-1213 points -StartRef children at their reported WorktreeBranch.
        // CARD-1213 S2 adds the git-common sentence: delegates carry it, briefs do not restate it.
        // CARD-1330 removes 197 unpinned characters so the 500-char argv reserve holds. The keep-six line stays.
        // CARD-1332 moves five procedures to owner anchors in docs/orchestration-loop.md; the bundle keeps
        // rules and pointers (normalised body 10,822 of its 11,500 allocation). The keep-six line stays.
        // CARD-1366 deliberately restores explicit Mutation tiers with matrix/pin and bound-pack
        // qualifications: normalised body 11,077 of 11,500; transport and reserve limits stay fixed.
        // CARD-1469 adds the operator standing-operating-rules pointer paragraph (full text at
        // docs/orchestration-loop.md#standing-operating-rules): normalised body 11,389 of 11,500.
        // CARD-1469 repair (Review 6bebf489 F1) rewrites rule 6 and adds its no-Reply-to-a-sync-blocked-report
        // prohibition to the pointer paragraph: normalised body 11,477 of 11,500.
        Convert.ToHexString(SHA256.HashData(orchestrator)).ToLowerInvariant()
            .ShouldBe("ded83f88b39699663c0a7a7434d52eb040d420a8497124438a5c564d8e7a556a", "orchestrator-approved-bytes");
    }
}
