using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>Executable instruction regressions, not evidence of model obedience or delivery.</summary>
[Category("Unit")]
public sealed class PostLandMutationContractTests
{
    [Test]
    public void C478_V11_ComposedContractsKeepOrdinaryReviewBeforePublication()
    {
        string Compose(AgentTaskRole role) => InstructionBundleComposer.Compose(
            InstructionBundles.ForDelegate(AgentTaskKind.Worker, role)).Text;
        var code = Compose(AgentTaskRole.Code);
        code.ShouldContain("next: review when implementation and ordinary V/R are complete, even with zero PCs");
        code.ShouldContain("original Code task ID (landing owner)");
        code.ShouldContain("pending for Mutation");
        code.ShouldNotContain("next: mutation when");
        var review = Compose(AgentTaskRole.Review);
        review.ShouldContain("Executed PCs are not a prerequisite");
        review.ShouldContain("Reject a missing producer-to-recipient test");
        review.ShouldContain("next: land when there are no defects");
        review.ShouldContain("original Code landing owner");
        var mutation = Compose(AgentTaskRole.Mutation);
        foreach (var contract in new[] { "SourceLanding Worktree", "HEAD=L", "clean tracked source/index",
                     "Never commit or push", "next: none", "next: decide", "every PC-n", "separately named variant",
                     "external executor", "irreversible task seal", "every accepted attempt", "SOURCELANDING MUTATION EXCEPTION" })
            mutation.ShouldContain(contract, Case.Insensitive);
        mutation.ShouldNotContain("next: land after");
        mutation.ShouldNotContain("-Role Mutation -Shared");
    }

    [Test]
    [Arguments("docs/orchestration-loop.md")]
    [Arguments(".claude/skills/antiphon-delegate/SKILL.md")]
    public void C478_V11_ActiveRecipeHasDurableCompanionAndExplicitContinuation(string path)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        root.ShouldNotBeNull();
        var text = File.ReadAllText(Path.Combine(root.FullName, path));
        foreach (var contract in new[] { "post-land-verification:<original-code-task-guid>", "publication pending",
                     "preserve existing", "HasPublication", "L=VerifiedSourceSha", "-SourceLanding <operation-guid>",
                     "-CleanupVerification <mutation-task-id>", "same commissioning project", "including Blocked",
                     "never PC-clean", "Failed/Canceled", "external executor", "NeedsDecision", "O2/L2",
                     "No force or recursive deletion", "health alone is insufficient" })
            text.ShouldContain(contract, Case.Insensitive);
        foreach (var obsolete in new[] { "-Role Mutation -Shared", "next: mutation even for zero-PC",
                     "Review — after Mutation", "Code requested Review through Mutation", "retained Code Shared" })
            text.ShouldNotContain(obsolete);
    }

    [Test]
    public void C478_V11_ActiveContractAndVocabulary() =>
        C478_V11_ComposedContractsKeepOrdinaryReviewBeforePublication();

    [Test]
    public void C478_G162_CodeHandsOffReview() =>
        InstructionBundleComposer.Compose(InstructionBundles.ForDelegate(AgentTaskKind.Worker, AgentTaskRole.Code))
            .Text.ShouldContain("next: review when implementation and ordinary V/R are complete, even with zero PCs");

    [Test]
    public void C478_G163_ReviewHasNoExecutedPcPrerequisite() =>
        InstructionBundleComposer.Compose(InstructionBundles.ForDelegate(AgentTaskKind.Worker, AgentTaskRole.Review))
            .Text.ShouldContain("Executed PCs are not a prerequisite");

    [Test]
    public void C478_G166_SnapshotCommitException() =>
        InstructionBundleComposer.Compose(InstructionBundles.ForDelegate(AgentTaskKind.Worker, AgentTaskRole.Mutation))
            .Text.ShouldContain("SOURCELANDING MUTATION EXCEPTION", Case.Insensitive);

    [Test]
    public void C478_G167_CleanNoneFindingDecide()
    {
        var mutation = InstructionBundleComposer.Compose(InstructionBundles.ForDelegate(AgentTaskKind.Worker, AgentTaskRole.Mutation)).Text;
        mutation.ShouldContain("next: none", Case.Insensitive);
        mutation.ShouldContain("next: decide", Case.Insensitive);
    }

    [Test]
    public void C478_G178_LegacyTokensRemain()
    {
        PipelineHandoff.TryParse("--- next stage ---\nnext: mutation\n").Kind.ShouldBe(PipelineHandoffKind.Mutation);
        PipelineHandoff.TryParse("--- next stage ---\nnext: verify\n").Kind.ShouldBe(PipelineHandoffKind.Review);
        PipelineHandoff.TryParse("--- next stage ---\nnext: land\n").Kind.ShouldBe(PipelineHandoffKind.Land);
    }

    [Test]
    public void C478_G230_ExternalExecutorContract()
    {
        var mutation = InstructionBundleComposer.Compose(InstructionBundles.ForDelegate(AgentTaskKind.Worker, AgentTaskRole.Mutation)).Text;
        mutation.ShouldContain("external executor", Case.Insensitive);
    }
}
