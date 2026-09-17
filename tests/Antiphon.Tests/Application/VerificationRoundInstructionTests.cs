using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0544 V-8 / G-62..G-66, G-89. The composed Code and Review instruction contracts for the
/// initial/default Final round, a cumulative Interim round, and the evidence a Review must demand.
/// Static contract text: proof of what an agent is told, never proof that it obeyed.
/// </summary>
[Category("Unit")]
public sealed class VerificationRoundInstructionTests
{
    private static string Compose(AgentTaskRole role) =>
        InstructionBundleComposer.Compose(InstructionBundles.ForDelegate(AgentTaskKind.Worker, role)).Text;

    [Test]
    public void C544_InitialFullContract()
    {
        var code = Compose(AgentTaskRole.Code);
        foreach (var required in new[]
                 {
                     "Unit plus the named affected integration classes",
                     "Final (default, first round): whole Unit lane, every full affected class, every ordinary V/R, required manual work.",
                     "Run each V-n and R-n the round requires; report every ID and actual outcome.",
                 })
            code.ShouldContain(required, customMessage: $"Code contract row: {required}");
        code.ShouldNotContain("selected-only", customMessage: "the default round must not be a selected-only sweep");
    }

    [Test]
    public void C544_FinalReviewContract()
    {
        var review = Compose(AgentTaskRole.Review);
        foreach (var required in new[]
                 {
                     "A Final Review reruns the complete ordinary scope itself, including every row an Interim round deferred;",
                     "an Interim pass never discharges it.",
                     "next: land when there are no defects and this was a Final Review;",
                     "review (Final) when a clean Interim;",
                     "ordinaryScopeCompleted: <Full|Interim|None>",
                 })
            review.ShouldContain(required, customMessage: $"Review contract row: {required}");
    }

    [Test]
    public void C544_CumulativeSelectionContract()
    {
        var code = Compose(AgentTaskRole.Code);
        foreach (var required in new[]
                 {
                     "Interim (explicit only): cumulative changed cases since the full baseline incl. earlier repair cases",
                     "unresolved-finding tests",
                 })
            code.ShouldContain(required, customMessage: $"cumulative Interim row: {required}");
        code.ShouldNotContain("since the last commit");
    }

    [Test]
    public void C544_AdjacentSmokeContract()
    {
        var code = Compose(AgentTaskRole.Code);
        foreach (var required in new[]
                 {
                     "named adjacent smoke",
                     "unbounded shared impact needs Final.",
                     "List deferred-to-final IDs; never mark them passed.",
                 })
            code.ShouldContain(required, customMessage: $"adjacent smoke row: {required}");
    }

    [Test]
    public void C544_ManualAndPcContract()
    {
        var review = Compose(AgentTaskRole.Review);
        review.ShouldContain("Required manual work stays pending and nightly green never satisfies manual or PC checks.",
            customMessage: "Review row: manual/PC not credited by nightly");
        review.ShouldContain("PCs stay pending", customMessage: "Review row: PCs pending");
        var code = Compose(AgentTaskRole.Code);
        code.ShouldContain("Report every PC-n/variant pending for Mutation", customMessage: "Code row: PCs pending for Mutation");
        code.ShouldContain("commissions SourceLanding Mutation", customMessage: "Code row: SourceLanding Mutation owner");
    }

    [Test]
    public void C544_ExecutionEvidenceContract()
    {
        var review = Compose(AgentTaskRole.Review);
        foreach (var required in new[]
                 {
                     "Require fresh executed identities and nonzero counts;",
                     "exit 0, --list-tests or missing parameter rows are not evidence.",
                 })
            review.ShouldContain(required, customMessage: $"execution evidence row: {required}");
        Compose(AgentTaskRole.Code).ShouldContain("inspect fresh TRX for each intended class/method and nonzero counts");
    }
}
