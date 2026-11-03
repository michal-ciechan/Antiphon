namespace Antiphon.Server.Application.Services;

/// <summary>CARD-1647 item 1 (red-first seam; the rule lands in the next commit).</summary>
public static class ReviewRoundGuard
{
    public const string Code = "review_round_limit";

    public static string? Evaluate(string? cardIdentifier, string goal, IReadOnlyList<string> completedReviewShortIds) => null;
}
