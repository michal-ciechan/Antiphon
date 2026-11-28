using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Review.Workflows;

internal enum ReviewGateTransition
{
    FollowEdge,
    StartIteration,
    Close,
}

/// <summary>Transitions the human decision API can commit for a workflow gate.</summary>
internal sealed record ReviewGateRule(ReviewGateDecision Decision, string Outcome, ReviewGateTransition Transition)
{
    internal const string ApproveOutcome = "approve";

    internal static IReadOnlyList<ReviewGateRule> All { get; } = Array.AsReadOnly<ReviewGateRule>(
    [
        new(ReviewGateDecision.Approve, ApproveOutcome, ReviewGateTransition.FollowEdge),
        new(ReviewGateDecision.AnotherRound, "another-round", ReviewGateTransition.StartIteration),
        new(ReviewGateDecision.Close, "close", ReviewGateTransition.Close),
    ]);

    internal static ReviewGateRule? Find(string outcome) => All.FirstOrDefault(rule => rule.Outcome == outcome);
}
