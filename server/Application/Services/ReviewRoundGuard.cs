using System.Text.RegularExpressions;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1647 item 1 (CARD-1635 item 4). docs/orchestration-loop.md#review-rounds caps code Review at
/// three rounds per subject and stops for the operator after round 3. The server cannot tell which
/// subject a new Review reviews, so it flags rather than refuses: a brief that declares round 4 or
/// later, or an undeclared Review on a card that already has three completed Reviews. The warning
/// reaches the caller at creation and the task timeline; it never blocks the dispatch.
/// </summary>
public static partial class ReviewRoundGuard
{
    public const string Code = "review_round_limit";
    public const int MaxRounds = 3;
    /// <summary>The SQL comment on the guard's card query, so a test can fault exactly that query.</summary>
    public const string QueryTag = "ReviewRoundGuard";

    [GeneratedRegex(@"^\s*round:\s*(\d{1,4})\b", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex DeclaredRoundLine();

    /// <summary>The first <c>round: N</c> line in the brief, as the review-rounds brief lines write it.</summary>
    public static int? DeclaredRound(string goal) =>
        DeclaredRoundLine().Match(goal) is { Success: true } match ? int.Parse(match.Groups[1].Value) : null;

    public static string? Evaluate(string? cardIdentifier, string goal, IReadOnlyList<string> completedReviewShortIds)
    {
        const string stop = "Review rounds stop at three per subject (docs/orchestration-loop.md#review-rounds): "
            + "surface the open blockers and the unreviewed range to the operator (next: decide) instead of another round.";
        if (DeclaredRound(goal) is { } declared)
            return declared > MaxRounds ? $"{Code}: this Review brief declares round {declared}. {stop}" : null;
        if (completedReviewShortIds.Count < MaxRounds)
            return null;
        return $"{Code}: {cardIdentifier ?? "this card"} already has {completedReviewShortIds.Count} completed Review tasks "
            + $"({string.Join(", ", completedReviewShortIds)}) and this brief declares no round. If they reviewed this subject "
            + $"this is round {completedReviewShortIds.Count + 1}. {stop}";
    }

    public static async Task<string?> EvaluateAsync(
        AppDbContext db, Guid? cardId, string? cardIdentifier, string goal, CancellationToken ct)
    {
        if (cardId is not Guid card)
            return null;
        var completed = await db.AgentTasks.AsNoTracking().TagWith(QueryTag)
            .Where(t => t.CardId == card && t.Role == AgentTaskRole.Review && t.Status == AgentTaskStatus.Succeeded)
            .OrderBy(t => t.CreatedAt)
            .Select(t => t.Id)
            .ToListAsync(ct);
        return Evaluate(cardIdentifier, goal, completed.Select(DelegationReportFormatter.Short).ToList());
    }
}
